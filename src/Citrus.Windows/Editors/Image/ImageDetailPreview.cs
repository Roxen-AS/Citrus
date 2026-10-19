using Citrus.Conversion.Editing.Image;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Image;

internal static class ImageDetailPreview
{
    public static PreviewRequest Create(ImageRecipe recipe, SizeI source, long generation, SizeI bounds)
    {
        var (_, output) = ImageGeometryMapper.Resolve(recipe, source);
        int width = Math.Min(bounds.Width,output.Width), height = Math.Min(bounds.Height,output.Height);
        int left = (output.Width-width)/2, top = (output.Height-height)/2;
        // SourceTile is in S, but native detail must fit bounds in final C pixels. A tiny inward
        // inset stabilizes the renderer's outward floor/ceil against inverse-map floating error.
        const double inset = .000001;
        PointD[] corners = [new(left+inset,top+inset),new(left+width-inset,top+inset),
            new(left+width-inset,top+height-inset),new(left+inset,top+height-inset)];
        var mapped = corners.Select(p => ImageGeometryMapper.MapFromCanvas(recipe,source,p)).ToArray();
        double x = mapped.Min(p => p.X), y = mapped.Min(p => p.Y);
        RectD tile = new(x,y,mapped.Max(p => p.X)-x,mapped.Max(p => p.Y)-y);
        return new(generation,bounds,null,null,tile);
    }
}
