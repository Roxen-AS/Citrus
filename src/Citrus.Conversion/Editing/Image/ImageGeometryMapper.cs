using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Image;

/// <summary>Pixel-edge coordinates: EXIF-normalized S → flips/CW quarter turns → rounded crop → final C.</summary>
public static class ImageGeometryMapper
{
    public static PointD MapToCanvas(ImageRecipe recipe, SizeI sourceSize, PointD sourcePoint)
    {
        var g = recipe.Geometry; var (crop, size) = Resolve(recipe, sourceSize);
        double x = g.FlipX ? sourceSize.Width - sourcePoint.X : sourcePoint.X;
        double y = g.FlipY ? sourceSize.Height - sourcePoint.Y : sourcePoint.Y;
        (x, y) = g.QuarterTurns switch { 1 => (sourceSize.Height - y, x), 2 => (sourceSize.Width - x, sourceSize.Height - y), 3 => (y, sourceSize.Width - x), _ => (x, y) };
        var placement=ImageCanvasPlacement.Resolve(recipe.Canvas,size);
        return new(placement.X+(x - crop.X) * placement.Width / crop.Width, placement.Y+(y - crop.Y) * placement.Height / crop.Height);
    }
    public static PointD MapFromCanvas(ImageRecipe recipe, SizeI sourceSize, PointD canvasPoint)
    {
        var g = recipe.Geometry; var (crop, size) = Resolve(recipe, sourceSize);
        var placement=ImageCanvasPlacement.Resolve(recipe.Canvas,size);
        double x = (canvasPoint.X-placement.X) * crop.Width / placement.Width + crop.X, y = (canvasPoint.Y-placement.Y) * crop.Height / placement.Height + crop.Y;
        (x, y) = g.QuarterTurns switch { 1 => (y, sourceSize.Height - x), 2 => (sourceSize.Width - x, sourceSize.Height - y), 3 => (sourceSize.Width - y, x), _ => (x, y) };
        return new(g.FlipX ? sourceSize.Width - x : x, g.FlipY ? sourceSize.Height - y : y);
    }
    public static (RectD Crop, SizeI OutputSize) Resolve(ImageRecipe recipe, SizeI sourceSize)
    {
        CheckSize(sourceSize); var g = recipe.Geometry;
        if (g.QuarterTurns is < 0 or > 3) throw new ArgumentException("Quarter turns must be 0..3.");
        int width = (g.QuarterTurns % 2 == 0) ? sourceSize.Width : sourceSize.Height, height = (g.QuarterTurns % 2 == 0) ? sourceSize.Height : sourceSize.Width;
        var r = g.Crop ?? new RectD(0, 0, width, height);
        if (!double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Width) || !double.IsFinite(r.Height) || r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0 || r.X + r.Width > width || r.Y + r.Height > height)
            throw new ArgumentException("Crop must fit the actual oriented source.");
        double left = Math.Clamp(Math.Floor(r.X), 0, width), top = Math.Clamp(Math.Floor(r.Y), 0, height), right = Math.Clamp(Math.Ceiling(r.X + r.Width), 0, width), bottom = Math.Clamp(Math.Ceiling(r.Y + r.Height), 0, height);
        var crop = new RectD(left, top, right - left, bottom - top);
        var output = g.OutputSize ?? new SizeI((int)crop.Width, (int)crop.Height); CheckSize(output);
        if (!g.AllowUpscale && (output.Width > crop.Width || output.Height > crop.Height)) throw new ArgumentException("Output would upscale the crop. Enable upscaling explicitly.");
        return (crop, output);
    }
    internal static void CheckSize(SizeI size)
    {
        if (size.Width is <= 0 or > 16384 || size.Height is <= 0 or > 16384 || (long)size.Width * size.Height > 64_000_000) throw new NotSupportedException("Images must be at most 64 megapixels and 16384 pixels per side.");
    }
}
