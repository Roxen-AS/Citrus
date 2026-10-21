namespace Citrus.Core.Editing;

/// <summary>Display pixels, independent of preview size and letterboxing. MP4 crops keep even extents.</summary>
public static class VideoCropGeometry
{
    public static RectD Normalize(RectD rect, SizeI plane)
    {
        if (plane.Width < 2 || plane.Height < 2 || new[] { rect.X, rect.Y, rect.Width, rect.Height }.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Crop requires finite dimensions and a video of at least two pixels.");
        int width = Even(rect.Width, plane.Width), height = Even(rect.Height, plane.Height);
        return new(Math.Clamp(Math.Round(rect.X), 0, plane.Width - width), Math.Clamp(Math.Round(rect.Y), 0, plane.Height - height), width, height);
    }
    public static RectD Resize(RectD current, SizeI plane, double percent, bool width, double? ratio)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent), "Enter a percentage from 0 to 100.");
        double w = width ? plane.Width * percent / 100 : current.Width;
        double h = width ? current.Height : plane.Height * percent / 100;
        if (ratio is > 0)
        {
            if (width) h = w / ratio.Value; else w = h * ratio.Value;
            double scale = Math.Min(1, Math.Min(plane.Width / Math.Max(2, w), plane.Height / Math.Max(2, h)));
            w *= scale; h *= scale;
        }
        int evenWidth = Even(w, plane.Width), evenHeight = Even(h, plane.Height);
        return Normalize(new(current.X + (current.Width - evenWidth) / 2, current.Y + (current.Height - evenHeight) / 2, evenWidth, evenHeight), plane);
    }
    public static RectD Fit(SizeI plane, double? ratio)
    {
        double width = plane.Width, height = plane.Height;
        if (ratio is > 0) { width = Math.Min(width, height * ratio.Value); height = width / ratio.Value; }
        int w = Even(width, plane.Width), h = Even(height, plane.Height);
        return Normalize(new((plane.Width - w) / 2d, (plane.Height - h) / 2d, w, h), plane);
    }
    private static int Even(double value, int limit) => Math.Clamp((int)Math.Round(Math.Clamp(value, 2, limit) / 2) * 2, 2, limit / 2 * 2);
}
