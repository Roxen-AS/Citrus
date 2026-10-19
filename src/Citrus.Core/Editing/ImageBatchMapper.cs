using System.Collections.Immutable;

namespace Citrus.Core.Editing;

public static class ImageBatchMapper
{
    public static EditorDocument Map(ImageBatchTemplate template, EditorDocument target, SizeI targetNormalizedSize)
    {
        if (template.Document.Recipe is not ImageRecipe source || target.Recipe is not ImageRecipe image)
            throw new ArgumentException("Image batches require image documents.");
        RequireValid(template.Document); RequireValid(target);
        if (target.Sources.Concat(template.Document.Sources).GroupBy(s => s.Id).Any(g => g.Distinct().Count() != 1) ||
            target.Fonts.Concat(template.Document.Fonts).GroupBy(f => f.Id).Any(g => g.Distinct().Count() != 1))
            throw new ArgumentException("Conflicting captured identities cannot be combined.");
        ValidSize(template.RootSize); ValidSize(targetNormalizedSize);
        if (!Enum.IsDefined(template.Geometry)) throw new ArgumentException("Unknown geometry mapping.");
        var from = Rotated(template.RootSize,source.Geometry.QuarterTurns);
        var to = Rotated(targetNormalizedSize,source.Geometry.QuarterTurns);
        RectD? crop = source.Geometry.Crop;
        if (crop is { } original)
        {
            double sx = template.Geometry == ImageBatchGeometry.Relative ? (double)to.Width / from.Width : 1;
            double sy = template.Geometry == ImageBatchGeometry.Relative ? (double)to.Height / from.Height : 1;
            double left = Math.Floor(original.X * sx), top = Math.Floor(original.Y * sy);
            double right = Math.Ceiling((original.X + original.Width) * sx), bottom = Math.Ceiling((original.Y + original.Height) * sy);
            if (left < 0 || top < 0 || right > to.Width || bottom > to.Height || right <= left || bottom <= top)
                throw new ArgumentException("Crop does not fit this source.");
            crop = new(left,top,right-left,bottom-top);
        }
        var geometry = source.Geometry with { Crop = crop, OutputSize = image.Geometry.OutputSize };
        if (template.Resize is { } resize)
        {
            if (resize.Width is <= 0 || resize.Height is <= 0 || (resize.Width is null && resize.Height is null) || (!resize.PreserveAspectRatio && (resize.Width is null || resize.Height is null)))
                throw new ArgumentException("Resize requires positive dimensions.");
            double width = crop?.Width ?? to.Width, height = crop?.Height ?? to.Height;
            double outputWidth = resize.Width ?? width, outputHeight = resize.Height ?? height;
            if (resize.PreserveAspectRatio)
            {
                double scale = resize.Width is { } w && resize.Height is { } h ? Math.Min(w / width,h / height) : resize.Width is { } onlyWidth ? onlyWidth / width : resize.Height!.Value / height;
                if (!resize.AllowUpscale) scale = Math.Min(1,scale);
                outputWidth = width * scale; outputHeight = height * scale;
            }
            else if (!resize.AllowUpscale && (outputWidth > width || outputHeight > height)) throw new ArgumentException("Upscaling is disabled.");
            if (outputWidth > 16384 || outputHeight > 16384) throw new ArgumentException("Output dimensions exceed limits.");
            geometry = geometry with { OutputSize = new(Math.Max(1,(int)Math.Round(outputWidth)),Math.Max(1,(int)Math.Round(outputHeight))), AllowUpscale = resize.AllowUpscale };
        }
        var masks = image.Masks;
        var annotations = image.Annotations;
        if (template.CopyLayers)
        {
            double sx = (double)targetNormalizedSize.Width / template.RootSize.Width, sy = (double)targetNormalizedSize.Height / template.RootSize.Height;
            masks = [.. source.Masks.Select(m => m with { Bounds = Scale(m.Bounds,sx,sy), Radius = m.Radius * Math.Sqrt(sx * sy) })];
            SizeI oldCanvas = source.Canvas?.Size ?? source.Geometry.OutputSize ?? new((int)(source.Geometry.Crop?.Width ?? from.Width),(int)(source.Geometry.Crop?.Height ?? from.Height));
            SizeI newCanvas = source.Canvas?.Size ?? geometry.OutputSize ?? new((int)(crop?.Width ?? to.Width),(int)(crop?.Height ?? to.Height));
            sx = (double)newCanvas.Width / oldCanvas.Width; sy = (double)newCanvas.Height / oldCanvas.Height;
            double radius = Math.Sqrt(sx * sy);
            annotations = [.. source.Annotations.Select(a => a with
            {
                Geometry = a.Geometry switch
                {
                    StrokeGeometry stroke => stroke with { Points = [.. stroke.Points.Select(p => new PointD(p.X*sx,p.Y*sy))] },
                    ShapeGeometry shape => shape with { Bounds = Scale(shape.Bounds,sx,sy), Radius = shape.Radius * radius },
                    TextGeometry text => text with { Origin = new(text.Origin.X*sx,text.Origin.Y*sy), WrapWidth = text.WrapWidth*sx },
                    NumberGeometry number => number with { Bounds = Scale(number.Bounds,sx,sy) },
                    InsetImageGeometry inset => inset with { Bounds = Scale(inset.Bounds,sx,sy), SourceId = inset.SourceId == source.SourceId ? image.SourceId : inset.SourceId },
                    MagnifierGeometry magnifier => magnifier with { SampleBounds = Scale(magnifier.SampleBounds,sx,sy), LensBounds = Scale(magnifier.LensBounds,sx,sy) },
                    _ => throw new ArgumentException("Unknown annotation.")
                },
                Style = a.Style with { StrokeWidth = a.Style.StrokeWidth*radius, Font = a.Style.Font is { } font ? font with { SizePixels = font.SizePixels*radius, LineHeight = font.LineHeight*radius } : null }
            })];
        }
        var canvas = source.Canvas;
        if (canvas?.Background is ImageBackground background && background.SourceId == source.SourceId)
            canvas = canvas with { Background = background with { SourceId = image.SourceId } };
        var recipe = source with { SourceId = image.SourceId, Geometry = geometry, Masks = masks, Annotations = annotations, Canvas = canvas };
        var referenced = EditorRecipeReferences.Sources(recipe).ToHashSet();
        var fontIds = EditorRecipeReferences.Fonts(recipe).ToHashSet();
        var available = target.Sources.Concat(template.Document.Sources.Where(s => s.Id != source.SourceId)).GroupBy(s => s.Id).Select(g => g.First());
        var mapped = target with
        {
            Recipe = recipe,
            Sources = [.. available.Where(s => referenced.Contains(s.Id))],
            Fonts = [.. target.Fonts.Concat(template.Document.Fonts).GroupBy(f => f.Id).Select(g => g.First()).Where(f => fontIds.Contains(f.Id))],
            Export = template.Document.Export with { OutputStem = target.Export.OutputStem, DestinationDirectory = target.Export.DestinationDirectory,
                FormatId = template.PreserveSourceFormat ? Path.GetExtension(target.Sources.Single(s => s.Id == image.SourceId).Path)[1..].ToLowerInvariant() : template.Document.Export.FormatId }
        };
        RequireValid(mapped);
        return mapped;
    }
    private static RectD Scale(RectD r,double x,double y) => new(r.X*x,r.Y*y,r.Width*x,r.Height*y);
    private static SizeI Rotated(SizeI size,int turns) => turns % 2 == 0 ? size : new(size.Height,size.Width);
    private static void ValidSize(SizeI size) { if (size.Width <= 0 || size.Height <= 0 || size.Width > 16384 || size.Height > 16384 || (long)size.Width * size.Height > 64_000_000) throw new ArgumentException("Invalid source size."); }
    private static void RequireValid(EditorDocument doc) { if (EditorRecipeValidator.Validate(doc) is { Length: > 0 } errors) throw new ArgumentException(string.Join("; ",errors.Select(e => e.FieldPath + ": " + e.Message))); }
}
