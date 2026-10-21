using System;
using System.Collections.Generic;
using Citrus.Core;

namespace Citrus.Windows.Tests;

internal sealed record GlassCaptureWindow(long Handle, ScreenRect Bounds, bool Visible, bool BoundsKnown,
    GlassWindowIdentity? Identity = null, string? IdentityReadFailure = null);
internal sealed record GlassCaptureSnapshot(ScreenRect Crop, ScreenRect WorkArea, long Background, long Wheel,
    long Companion, IReadOnlyList<GlassCaptureWindow> Windows, bool Complete, GlassDesktopSnapshot? Desktop = null, GlassInputOwnership? Input = null, GlassWindowIdentity? InputMismatchWindow = null,
    long DragCapture = 0, GlassWindowIdentity? DragCaptureIdentity = null, ScreenRect? SurfaceCrop = null,
    int RefusalIdentityLimit = 0, bool RefusalIdentitiesTruncated = false);
internal sealed record GlassDesktopSnapshot(bool UserInteractive, bool QueriesSucceeded, string WindowStation,
    string ThreadDesktop, string InputDesktop, bool ThreadIsInputDesktop, uint ProcessSession, uint ActiveConsoleSession,
    int WtsConnectionState, long Foreground, uint? ForegroundSession, long ThreadDesktopHandle = 0,
    long InputDesktopHandle = 0, long WindowStationHandle = 0, int NativeError = 0, GlassWindowIdentity? ForegroundWindow = null, string QueryDiagnostic = "");
internal sealed record GlassInputOwnership(long CheckedPixels, long RequiredPixels, bool Complete, int? FailedX = null,
    int? FailedY = null, long ActualRoot = 0, string? Failure = null);
internal sealed record GlassWindowIdentity(long Handle, long RootHandle, string ClassName, uint ProcessId, string ProcessName,
    uint ThreadId, uint? SessionId, bool BoundsKnown, ScreenRect Bounds, long Style, long ExtendedStyle, string DesktopName,
    DateTime? ProcessStartedUtc = null);
internal sealed record GlassCapturePreflight(bool Allowed, string Reason);

internal static class GlassCaptureOwnership
{
    internal static GlassCapturePreflight Check(GlassCaptureSnapshot scene)
    {
        var desktop = CheckDesktop(scene.Desktop);
        if (!desktop.Allowed) return desktop;
        if (scene.DragCapture != 0 && (scene.DragCaptureIdentity is not { } identity ||
            identity.Handle != scene.DragCapture || identity.RootHandle != scene.DragCapture ||
            identity.ProcessId != Environment.ProcessId || !identity.BoundsKnown || !Contains(identity.Bounds, scene.Crop)))
            return new(false, "DragCaptureIdentityUnavailable");
        if (scene.Input is not { Complete: true, Failure: null } input ||
            input.RequiredPixels != (long)(scene.Crop.Width * scene.Crop.Height) || input.CheckedPixels != input.RequiredPixels)
            return new(false, "InputCropOwnershipUnavailable");
        if (!scene.Complete) return new(false, "IncompleteVisualZOrder");
        if (!Valid(scene.Crop) || scene.Crop.Width * scene.Crop.Height > 16_777_216)
            return new(false, "InvalidOrUnboundedCrop");
        var surfaceCrop = scene.SurfaceCrop ?? scene.Crop;
        if (!Contains(scene.Crop, surfaceCrop)) return new(false, "SurfaceOutsideSampledCrop");
        if (!Contains(scene.WorkArea, scene.Crop)) return new(false, "CropOutsideWorkArea");
        int background = -1, wheel = -1, companion = -1, dragCapture = -1;
        for (int index = 0; index < scene.Windows.Count; index++)
        {
            var window = scene.Windows[index];
            if (!window.Visible) continue;
            if (window.Handle == scene.Background) background = index;
            if (window.Handle == scene.Wheel) wheel = index;
            if (scene.Companion != 0 && window.Handle == scene.Companion) companion = index;
            if (scene.DragCapture != 0 && window.Handle == scene.DragCapture) dragCapture = index;
        }
        if (background < 0 || !scene.Windows[background].BoundsKnown || !Contains(scene.Windows[background].Bounds, scene.Crop))
            return new(false, "CropOutsideVisibleOwnedBackground");
        if (wheel < 0 || wheel >= background || !scene.Windows[wheel].BoundsKnown || !Contains(scene.Windows[wheel].Bounds, surfaceCrop))
            return new(false, "WheelNotCoveringCropAboveBackground");
        if (companion >= 0 && (companion <= wheel || companion >= background))
            return new(false, "CompanionVisualZOrderMismatch");
        if (scene.DragCapture != 0 && (dragCapture < 0 || dragCapture >= background ||
            !scene.Windows[dragCapture].BoundsKnown || !Contains(scene.Windows[dragCapture].Bounds, scene.Crop)))
            return new(false, "DragCaptureNotCoveringCropAboveBackground");
        for (int index = 0; index < background; index++)
        {
            var window = scene.Windows[index];
            if (!window.Visible) continue;
            if (!window.BoundsKnown || !Valid(window.Bounds)) return new(false, "UnknownVisibleWindowBounds");
            if (window.Handle == scene.Wheel || window.Handle == scene.Companion || window.Handle == scene.DragCapture) continue;
            if (Intersects(window.Bounds, scene.Crop)) return new(false, "UnrelatedVisualOccluder");
        }
        return new(true, "EntireCropOwnedBeforeCapture");
    }
    internal static GlassCapturePreflight CheckDesktop(GlassDesktopSnapshot? desktop)
    {
        if (desktop is null || !desktop.QueriesSucceeded || !desktop.UserInteractive)
            return new(false, "InteractiveDesktopIdentityUnavailable");
        if (!string.Equals(desktop.WindowStation, "WinSta0", StringComparison.OrdinalIgnoreCase) || !string.Equals(desktop.ThreadDesktop, desktop.InputDesktop, StringComparison.OrdinalIgnoreCase) || !desktop.ThreadIsInputDesktop)
            return new(false, "ThreadIsNotActualInputDesktop");
        if (desktop.ProcessSession == 0 || desktop.ProcessSession != desktop.ActiveConsoleSession || desktop.WtsConnectionState != 0)
            return new(false, "SessionIsNotActiveInteractiveConsole");
        if (desktop.Foreground == 0 || desktop.ForegroundSession != desktop.ProcessSession)
            return new(false, "ForegroundIsNotInInteractiveSession");
        return new(true, "ActualInteractiveInputDesktop");
    }
    private static bool Valid(ScreenRect rectangle) => double.IsFinite(rectangle.Left) && double.IsFinite(rectangle.Top) &&
        double.IsFinite(rectangle.Right) && double.IsFinite(rectangle.Bottom) && rectangle.Width > 0 && rectangle.Height > 0;
    private static bool Contains(ScreenRect outer, ScreenRect inner) => Valid(outer) && Valid(inner) &&
        inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
    private static bool Intersects(ScreenRect first, ScreenRect second) => first.Left < second.Right && first.Right > second.Left &&
        first.Top < second.Bottom && first.Bottom > second.Top;
}
