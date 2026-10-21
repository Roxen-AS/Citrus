namespace Citrus.Core.Editing;

/// <summary>Source photo pixels to final canvas pixels, independent of preview raster scale.</summary>
public static class ImageCanvasPlacement
{
    public static RectD Resolve(CanvasSpec? canvas,SizeI photo)
    {
        if(canvas is null)return new(0,0,photo.Width,photo.Height);
        var p=canvas.Placement;double scale=canvas.Fit==ImageFit.Cover?Math.Max(p.Width/photo.Width,p.Height/photo.Height):Math.Min(p.Width/photo.Width,p.Height/photo.Height);
        double w=canvas.Fit==ImageFit.Stretch?p.Width:photo.Width*scale,h=canvas.Fit==ImageFit.Stretch?p.Height:photo.Height*scale;
        return new(p.X+(p.Width-w)/2,p.Y+(p.Height-h)/2,w,h);
    }
    public static EditorDocument Change(EditorDocument document,SizeI photo,CanvasSpec? canvas)
    {
        var recipe=document.Recipe as ImageRecipe??throw new ArgumentException("Image recipe required.");
        RectD from=Resolve(recipe.Canvas,photo),to=Resolve(canvas,photo);
        double sx=to.Width/from.Width,sy=to.Height/from.Height,scale=Math.Sqrt(sx*sy);
        PointD Point(PointD p)=>new(to.X+(p.X-from.X)*sx,to.Y+(p.Y-from.Y)*sy);
        RectD Rect(RectD r){var p=Point(new(r.X,r.Y));return new(p.X,p.Y,r.Width*sx,r.Height*sy);}
        var next=recipe with{Canvas=canvas,Annotations=[..recipe.Annotations.Select(a=>a with{
            Geometry=a.Geometry switch{
                StrokeGeometry g=>g with{Points=[..g.Points.Select(Point)]},
                ShapeGeometry g=>g with{Bounds=Rect(g.Bounds),Radius=g.Radius*scale},
                TextGeometry g=>g with{Origin=Point(g.Origin),WrapWidth=g.WrapWidth*sx},
                NumberGeometry g=>g with{Bounds=Rect(g.Bounds)},
                InsetImageGeometry g=>g with{Bounds=Rect(g.Bounds)},
                MagnifierGeometry g=>g with{SampleBounds=Rect(g.SampleBounds),LensBounds=Rect(g.LensBounds)},
                _=>throw new ArgumentException("Unknown annotation geometry.")},
            Style=a.Style with{StrokeWidth=a.Style.StrokeWidth*scale,Font=a.Style.Font is {} f?f with{SizePixels=f.SizePixels*scale,LineHeight=f.LineHeight*scale}:null}
        })]};
        var referenced=EditorRecipeReferences.Sources(next).ToHashSet();
        return document with {Recipe=next,Sources=[..document.Sources.Where(s=>referenced.Contains(s.Id))]};
    }
}
