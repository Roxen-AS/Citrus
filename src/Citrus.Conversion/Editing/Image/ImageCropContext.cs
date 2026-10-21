using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Image;

/// <summary>A temporary full-G view for crop handles. The saved canvas and all real layers remain untouched.</summary>
public static class ImageCropContext
{
    public static EditorDocument Create(EditorDocument document,SizeI source)
    {
        var recipe=document.Recipe as ImageRecipe??throw new ArgumentException("Image recipe required.");
        var (crop,photo)=ImageGeometryMapper.Resolve(recipe,source);
        var plane=recipe.Geometry.QuarterTurns%2==0?source:new SizeI(source.Height,source.Width);
        // Mapping the old photo into its original G-plane crop rectangle reverses canvas
        // placement and output resizing in one affine transform, including label/font sizes.
        var context=ImageCanvasPlacement.Change(document,photo,new(plane,new TransparentBackground(),crop,ImageFit.Stretch,0,null));
        var mapped=(ImageRecipe)context.Recipe;
        mapped=mapped with{Canvas=null,Geometry=recipe.Geometry with{Crop=null,OutputSize=null},Annotations=[..mapped.Annotations.Where(FitsPreviewContract)]};
        var references=EditorRecipeReferences.Sources(mapped).ToHashSet();var fonts=EditorRecipeReferences.Fonts(mapped).ToHashSet();
        return context with{Recipe=mapped,Sources=[..context.Sources.Where(s=>references.Contains(s.Id))],Fonts=[..context.Fonts.Where(f=>fonts.Contains(f.Id))]};
    }
    private static bool FitsPreviewContract(Annotation annotation)
    {
        static bool Number(double value,double min,double max)=>double.IsFinite(value)&&value>=min&&value<=max;
        static bool Rect(RectD r)=>Number(r.X,0,16384)&&Number(r.Y,0,16384)&&Number(r.Width,double.Epsilon,16384)&&Number(r.Height,double.Epsilon,16384);
        if(!Number(annotation.Style.StrokeWidth,0,16384)||annotation.Style.Font is {} f&&(!Number(f.SizePixels,double.Epsilon,16384)||!Number(f.LineHeight,double.Epsilon,16384)))return false;
        return annotation.Geometry switch
        {
            ShapeGeometry s=>Rect(s.Bounds)&&Number(s.Radius,0,16384),NumberGeometry n=>Rect(n.Bounds),InsetImageGeometry i=>Rect(i.Bounds),
            MagnifierGeometry m=>Rect(m.SampleBounds)&&Rect(m.LensBounds),
            StrokeGeometry s=>s.Points.All(p=>Number(p.X,-16384,32768)&&Number(p.Y,-16384,32768)),
            TextGeometry t=>Number(t.Origin.X,-16384,32768)&&Number(t.Origin.Y,-16384,32768)&&Number(t.WrapWidth,double.Epsilon,16384),_=>false
        };
    }
}
