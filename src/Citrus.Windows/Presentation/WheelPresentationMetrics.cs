using Citrus.Core;

namespace Citrus.Windows.Presentation;

/// <summary>Canonical geometry remains unchanged; display it at a more compact size.</summary>
internal static class WheelPresentationMetrics
{
    internal const double Scale = .78;
    // Preserve the previous displayed label size while reducing the surrounding wheel.
    internal const double LabelCompensation = .85 / Scale;
    internal const double HubCapsuleWidth = 110;
    internal const double Width = WheelGeometry.Width * Scale;
    internal const double Height = WheelGeometry.Height * Scale;

    internal static double RasterScale(double monitorScale)
    {
        if (!double.IsFinite(monitorScale) || monitorScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorScale));
        // SetWindowPos receives the truncated physical width, not the rounded
        // nominal DIP width. Its capture resources must use the same raster key.
        double pixels = Math.Floor(Width * monitorScale);
        if (pixels < 1 || pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(monitorScale));
        return pixels / WheelGeometry.Width;
    }
}
