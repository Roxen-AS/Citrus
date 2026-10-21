namespace Citrus.Windows.Presentation;

internal readonly record struct GlassContrast(bool LightText, double ScrimOpacity);

/// <summary>Protects glyph-sized areas only; clear glass never receives a global wash.</summary>
internal static class GlassLabelContrast
{
    private static readonly double[] LinearChannels = Enumerable.Range(0, 256).Select(channel => Linear(channel / 255d)).ToArray();
    internal static GlassContrast Evaluate(byte[] pixels, int width, int height, double scale, Rect bounds)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 4) || !double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (bounds.IsEmpty) return new(false, 0);
        int left = Math.Clamp((int)Math.Floor(bounds.Left * scale), 0, width);
        int top = Math.Clamp((int)Math.Floor(bounds.Top * scale), 0, height);
        int right = Math.Clamp((int)Math.Ceiling(bounds.Right * scale), left, width);
        int bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * scale), top, height);
        // One-DIP sampling bounds the work for large accessibility text while still
        // seeing both strokes and whitespace in a dense underlying document.
        int step = Math.Max(1, (int)scale);
        double minimum = 1, maximum = 0, darkestChannel = 1, brightestChannel = 0;
        bool found = false;
        for (int y = top; y < bottom; y += step)
        for (int x = left; x < right; x += step)
        {
            int index = (y * width + x) * 4;
            if (pixels[index + 3] < 240) continue;
            double r = pixels[index + 2] / 255d, g = pixels[index + 1] / 255d, b = pixels[index] / 255d;
            double luminance = .2126 * LinearChannels[pixels[index + 2]] + .7152 * LinearChannels[pixels[index + 1]] + .0722 * LinearChannels[pixels[index]];
            minimum = Math.Min(minimum, luminance); maximum = Math.Max(maximum, luminance);
            darkestChannel = Math.Min(darkestChannel, Math.Min(r, Math.Min(g, b)));
            brightestChannel = Math.Max(brightestChannel, Math.Max(r, Math.Max(g, b)));
            found = true;
        }
        if (!found) return new(false, 0);
        bool light = 1.05 / (maximum + .05) > (minimum + .05) / .05;
        double current = light ? 1.05 / (maximum + .05) : (minimum + .05) / .05;
        if (current >= 4.5) return new(light, 0);
        // Conservative channel bound: the plate blends in sRGB, like WPF. This
        // also protects saturated colors without approximating linear blending.
        double low = 0, high = 1;
        for (int i = 0; i < 14; i++)
        {
            double opacity = (low + high) / 2;
            double channel = light ? brightestChannel * (1 - opacity) : darkestChannel * (1 - opacity) + opacity;
            double luminance = Linear(channel);
            double contrast = light ? 1.05 / (luminance + .05) : (luminance + .05) / .05;
            if (contrast < 4.5) low = opacity; else high = opacity;
        }
        return new(light, high);
    }

    private static double Linear(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
}
