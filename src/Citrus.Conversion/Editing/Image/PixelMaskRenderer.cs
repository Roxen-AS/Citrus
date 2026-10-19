using System.Collections.Immutable;
using Citrus.Core.Editing;
using ImageMagick;

namespace Citrus.Conversion.Editing.Image;

/// <summary>Masks replace pixels in the adjusted, cropped G raster before final resizing and annotations.</summary>
internal static class PixelMaskRenderer
{
    internal static ImmutableArray<string> Warnings(ImageRecipe recipe) => recipe.Masks.Any(m => m.Effect != MaskEffect.Solid)
        ? ["Gaussian and pixelate masks provide visual obscuring only; use a solid mask to remove original pixels."] : [];

    internal static void Apply(MagickImage image, ImageRecipe recipe, SizeI sourceSize, CancellationToken ct)
    {
        if (recipe.Masks.IsEmpty) return;
        // Mapper remains authoritative: substitute the actual working size for final C.
        var working = recipe with { Geometry = recipe.Geometry with { OutputSize = new((int)image.Width, (int)image.Height), AllowUpscale = true } };
        var (crop, _) = ImageGeometryMapper.Resolve(working, sourceSize);
        double scale = Math.Sqrt(image.Width / crop.Width * (image.Height / crop.Height));
        foreach (var mask in recipe.Masks)
        {
            ct.ThrowIfCancellationRequested();
            var b = mask.Bounds;
            if (b.X < 0 || b.Y < 0 || b.X + b.Width > sourceSize.Width || b.Y + b.Height > sourceSize.Height) throw new ArgumentException("Mask must fit normalized source-plane S.");
            var a = ImageGeometryMapper.MapToCanvas(working, sourceSize, new(b.X,b.Y));
            var z = ImageGeometryMapper.MapToCanvas(working, sourceSize, new(b.X+b.Width,b.Y+b.Height));
            int left = (int)Math.Clamp(Math.Floor(Math.Min(a.X,z.X)),0,image.Width), top = (int)Math.Clamp(Math.Floor(Math.Min(a.Y,z.Y)),0,image.Height);
            int right = (int)Math.Clamp(Math.Ceiling(Math.Max(a.X,z.X)),0,image.Width), bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(a.Y,z.Y)),0,image.Height);
            if (right <= left || bottom <= top) continue;
            using var region = new MagickImage(new MagickColor(mask.Fill.R,mask.Fill.G,mask.Fill.B,mask.Fill.A),(uint)(right-left),(uint)(bottom-top));
            region.Alpha(AlphaAction.Set);
            if (mask.Effect == MaskEffect.Gaussian)
            {
                using var blurred = image.Clone(); blurred.GaussianBlur(0,Math.Max(.01,mask.Radius * scale));
                blurred.Crop(new MagickGeometry(left,top,(uint)(right-left),(uint)(bottom-top))); blurred.ResetPage();
                region.Composite(blurred,CompositeOperator.Copy);
            }
            else if (mask.Effect == MaskEffect.Pixelate)
            {
                using var pixels = image.Clone(); pixels.Crop(new MagickGeometry(left,top,(uint)(right-left),(uint)(bottom-top))); pixels.ResetPage();
                int block = Math.Max(1,(int)Math.Round(mask.Radius * scale));
                pixels.Scale((uint)Math.Max(1,(right-left)/block),(uint)Math.Max(1,(bottom-top)/block));
                pixels.Sample((uint)(right-left),(uint)(bottom-top)); region.Composite(pixels,CompositeOperator.Copy);
            }
            else if (mask.Effect != MaskEffect.Solid) throw new ArgumentException("Unknown mask effect.");
            // Copy, not alpha-over: even a transparent fill cannot retain original RGB below it.
            if (!image.HasAlpha) image.Alpha(AlphaAction.Opaque);
            image.Composite(region,left,top,CompositeOperator.Copy);
        }
        ct.ThrowIfCancellationRequested();
    }

    internal static void RemoveOriginalMetadata(MagickImage image, ImageRecipe recipe)
    {
        // EXIF/JFIF/XMP/Photoshop profiles can carry original thumbnails; discard all source
        // metadata on any solid mask. PrepareOutput restores only the working ICC profile.
        if (recipe.Masks.Any(m => m.Effect == MaskEffect.Solid)) { image.Strip(); image.RemoveArtifact("png:exclude-chunk"); }
    }
}
