using System;
using System.Collections.Generic;
using Citrus.Core;

namespace Citrus.Windows.Tests;

internal sealed record GlassCaptureWindow(long Handle, ScreenRect Bounds, bool Visible, bool BoundsKnown);
internal sealed record GlassCaptureSnapshot(ScreenRect Crop, ScreenRect WorkArea, long Background, long Wheel,
    long Companion, IReadOnlyList<GlassCaptureWindow> Windows, bool Complete);
internal sealed record GlassCapturePreflight(bool Allowed, string Reason);

internal static class GlassCaptureOwnership
{
    internal static GlassCapturePreflight Check(GlassCaptureSnapshot scene)
    {
        if (!scene.Complete) return new(false, "IncompleteVisualZOrder");
        if (!Valid(scene.Crop) || scene.Crop.Width * scene.Crop.Height > 16_777_216)
            return new(false, "InvalidOrUnboundedCrop");
        if (!Contains(scene.WorkArea, scene.Crop)) return new(false, "CropOutsideWorkArea");
        int background = -1, wheel = -1, companion = -1;
        for (int index = 0; index < scene.Windows.Count; index++)
        {
            var window = scene.Windows[index];
            if (!window.Visible) continue;
            if (window.Handle == scene.Background) background = index;
            if (window.Handle == scene.Wheel) wheel = index;
            if (scene.Companion != 0 && window.Handle == scene.Companion) companion = index;
        }
        if (background < 0 || !scene.Windows[background].BoundsKnown || !Contains(scene.Windows[background].Bounds, scene.Crop))
            return new(false, "CropOutsideVisibleOwnedBackground");
        if (wheel < 0 || wheel >= background || !scene.Windows[wheel].BoundsKnown || !Contains(scene.Windows[wheel].Bounds, scene.Crop))
            return new(false, "WheelNotCoveringCropAboveBackground");
        if (companion >= 0 && (companion <= wheel || companion >= background))
            return new(false, "CompanionVisualZOrderMismatch");
        for (int index = 0; index < background; index++)
        {
            var window = scene.Windows[index];
            if (!window.Visible) continue;
            if (!window.BoundsKnown || !Valid(window.Bounds)) return new(false, "UnknownVisibleWindowBounds");
            if (window.Handle == scene.Wheel || window.Handle == scene.Companion) continue;
            if (Intersects(window.Bounds, scene.Crop)) return new(false, "UnrelatedVisualOccluder");
        }
        return new(true, "EntireCropOwnedBeforeCapture");
    }
    private static bool Valid(ScreenRect rectangle) => double.IsFinite(rectangle.Left) && double.IsFinite(rectangle.Top) &&
        double.IsFinite(rectangle.Right) && double.IsFinite(rectangle.Bottom) && rectangle.Width > 0 && rectangle.Height > 0;
    private static bool Contains(ScreenRect outer, ScreenRect inner) => Valid(outer) && Valid(inner) &&
        inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
    private static bool Intersects(ScreenRect first, ScreenRect second) => first.Left < second.Right && first.Right > second.Left &&
        first.Top < second.Bottom && first.Bottom > second.Top;
}
