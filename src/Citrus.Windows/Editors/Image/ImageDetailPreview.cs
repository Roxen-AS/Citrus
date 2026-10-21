using Citrus.Conversion.Editing.Image;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Image;

internal static class ImageDetailPreview
{
    public static PreviewRequest Create(ImageRecipe recipe, SizeI source, long generation, SizeI bounds)
    {
        var (_, output) = ImageGeometryMapper.Resolve(recipe, source);
        var placement=ImageCanvasPlacement.Resolve(recipe.Canvas,output);
        var canvas=recipe.Canvas?.Size??output;
        double minX=Math.Max(0,placement.X),minY=Math.Max(0,placement.Y),maxX=Math.Min(canvas.Width,placement.X+placement.Width),maxY=Math.Min(canvas.Height,placement.Y+placement.Height);
        double left,top,width,height;
        // Detail requests name final raster pixels. Keep them inside both the photo
        // and canvas; fractional centers must not add a pixel and trigger resampling.
        int innerLeft=(int)Math.Ceiling(minX),innerTop=(int)Math.Ceiling(minY),innerRight=(int)Math.Floor(maxX),innerBottom=(int)Math.Floor(maxY);
        if(innerRight>innerLeft&&innerBottom>innerTop)
        {
            width=Math.Min(bounds.Width,innerRight-innerLeft);height=Math.Min(bounds.Height,innerBottom-innerTop);
            left=innerLeft+(int)(innerRight-innerLeft-width)/2;top=innerTop+(int)(innerBottom-innerTop-height)/2;
        }
        else
        {
            // A subpixel photo has no complete interior raster pixel. Request its
            // visible footprint without moving either edge outside the source.
            width=Math.Min(bounds.Width,maxX-minX);height=Math.Min(bounds.Height,maxY-minY);
            left=minX+(maxX-minX-width)/2;top=minY+(maxY-minY-height)/2;
        }
        // SourceTile is in S, but native detail must fit bounds in final C pixels. A tiny inward
        // inset stabilizes the renderer's outward floor/ceil against inverse-map floating error.
        double inset = Math.Min(.000001,Math.Min(width,height)/4);
        PointD[] corners = [new(left+inset,top+inset),new(left+width-inset,top+inset),
            new(left+width-inset,top+height-inset),new(left+inset,top+height-inset)];
        var mapped = corners.Select(p => ImageGeometryMapper.MapFromCanvas(recipe,source,p)).ToArray();
        double x = mapped.Min(p => p.X), y = mapped.Min(p => p.Y);
        RectD tile = new(x,y,mapped.Max(p => p.X)-x,mapped.Max(p => p.Y)-y);
        return new(generation,bounds,null,null,tile);
    }
}
