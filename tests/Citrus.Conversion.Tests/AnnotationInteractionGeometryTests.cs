using System.Collections.Immutable;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Image;

namespace Citrus.Conversion.Tests;

public sealed class AnnotationInteractionGeometryTests
{
    private static readonly AnnotationStyle Ink = new(new(20,30,40,255),new(0,0,0,0),4,1,null);
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void Viewport_roundtrip_removes_letterbox_zoom_pan_and_dpi(double dpi)
    {
        var viewport = new ImageLayerViewport(new(1200,800),new(800/dpi,600/dpi),2,new(19,-7));
        var saved = new PointD(310,260); var screen = viewport.ToView(saved); var back = viewport.ToCanvas(screen);
        Assert.Equal(saved.X,back.X,8); Assert.Equal(saved.Y,back.Y,8);
    }
    [Fact]
    public void Hollow_shapes_and_strokes_use_geometry_not_their_bounding_box()
    {
        var shape = new Annotation(Guid.NewGuid(),true,new ShapeGeometry(new(10,10,80,40),true,0),Ink);
        Assert.False(ImageLayerGeometry.Hit(shape,new(10,10),1));
        Assert.False(ImageLayerGeometry.Hit(shape,new(50,30),1));
        Assert.True(ImageLayerGeometry.Hit(shape,new(50,10),1));
        var line = shape with {Geometry=new StrokeGeometry([new(0,0),new(100,100)],false,true)};
        Assert.False(ImageLayerGeometry.Hit(line,new(0,100),2)); Assert.True(ImageLayerGeometry.Hit(line,new(50,51),2));
    }
    [Theory]
    [InlineData(0,false,false)] [InlineData(1,true,false)] [InlineData(2,false,true)] [InlineData(3,true,true)]
    public void Masks_map_source_rectangles_through_rotation_crop_and_nonuniform_resize(int turns,bool flipX,bool flipY)
    {
        var recipe = Recipe() with {Geometry=new(turns,flipX,flipY,new(10,10,40,30),new(300,120),true)};
        var source = new SizeI(100,80); var rect = new RectD(20,25,9,12);
        var canvas = ImageLayerGeometry.MaskToCanvas(recipe,source,rect);
        var back = ImageLayerGeometry.MaskFromCanvas(recipe,source,canvas);
        Assert.Equal(rect.X,back.X,8); Assert.Equal(rect.Y,back.Y,8); Assert.Equal(rect.Width,back.Width,8); Assert.Equal(rect.Height,back.Height,8);
    }
    [Fact]
    public void Resizing_stroke_maps_all_saved_points_and_arrow_flags()
    {
        var layer = new Annotation(Guid.NewGuid(),true,new StrokeGeometry([new(10,20),new(30,40),new(50,20)],true,true),Ink);
        var result = ImageLayerGeometry.SetBounds(layer,new(100,200,80,60));
        var points = Assert.IsType<StrokeGeometry>(result.Geometry);
        Assert.Equal(new PointD(100,200),points.Points[0]); Assert.Equal(new PointD(140,260),points.Points[1]); Assert.True(points.ArrowStart); Assert.True(points.ArrowEnd);
    }
    [Fact]
    public void Hit_selection_uses_topmost_visible_layer_and_callout_numbers_do_not_renumber()
    {
        var first = new Annotation(Guid.NewGuid(),true,new NumberGeometry(new(1,1,20,20),2),Ink with {Fill=new(255,0,0,255)});
        var last = first with {Id=Guid.NewGuid(),Geometry=new NumberGeometry(new(1,1,20,20),7)};
        ImmutableArray<Annotation> all=[first,last];
        Assert.Equal(last.Id,ImageLayerGeometry.HitTop(all,new(10,10),1));
        Assert.Equal(8,ImageLayerGeometry.NextNumber(all));
        Assert.Equal(8,ImageLayerGeometry.NextNumber(all.Remove(first)));
        Assert.Equal(first.Id,ImageLayerGeometry.HitTop([first,last with {Visible=false}],new(10,10),1));
    }
    [Fact]
    public void Keyboard_nudge_is_exact_content_pixel_even_for_source_mask()
    {
        var r=Recipe() with {Geometry=new(1,true,false,new(10,10,40,30),new(200,90),true)};
        var before=ImageLayerGeometry.MaskToCanvas(r,new(100,80),new(30,20,10,10));
        var moved=ImageLayerGeometry.Translate(before,10,-1);
        var saved=ImageLayerGeometry.MaskFromCanvas(r,new(100,80),moved);
        var after=ImageLayerGeometry.MaskToCanvas(r,new(100,80),saved);
        Assert.Equal(before.X+10,after.X,8);Assert.Equal(before.Y-1,after.Y,8);
    }
    [Fact]
    public void Rounded_filled_shapes_exclude_the_cut_corner()
    {
        var rounded=new Annotation(Guid.NewGuid(),true,new ShapeGeometry(new(10,10,80,40),false,15),Ink with {Fill=new(255,0,0,255),StrokeWidth=0});
        Assert.False(ImageLayerGeometry.Hit(rounded,new(10,10),0));
        Assert.True(ImageLayerGeometry.Hit(rounded,new(25,11),0));
    }
    [Fact]
    public void Callout_number_exhaustion_produces_a_visible_validation_error()
    {
        var last=new Annotation(Guid.NewGuid(),true,new NumberGeometry(new(0,0,10,10),int.MaxValue),Ink);
        Assert.Throws<ArgumentException>(()=>ImageLayerGeometry.NextNumber([last]));
    }
    [Fact]
    public void Horizontal_arrow_resize_can_change_its_angle_without_nan_or_dip_storage()
    {
        var arrow=new Annotation(Guid.NewGuid(),true,new StrokeGeometry([new(10,20),new(110,20)],false,true),Ink);
        var resized=ImageLayerGeometry.SetBounds(arrow,new(50,60,200,100));
        var points=((StrokeGeometry)resized.Geometry).Points;
        Assert.Equal(new PointD(50,60),points[0]);Assert.Equal(new PointD(250,160),points[1]);
    }
    [Fact]
    public void A_one_content_pixel_mask_keeps_subpixel_source_bounds_after_upscaling()
    {
        var image=Recipe() with {Geometry=new(0,false,false,null,new(1000,800),true)};
        var saved=ImageLayerGeometry.ClampMaskToSource(ImageLayerGeometry.MaskFromCanvas(image,new(100,80),new(400,300,1,1)),new(100,80));
        Assert.Equal(.1,saved.Width,8);Assert.Equal(.1,saved.Height,8);
        Assert.Equal(1,ImageLayerGeometry.MaskToCanvas(image,new(100,80),saved).Width,8);
    }
    [Theory]
    [InlineData(90)] [InlineData(30)] [InlineData(45)] [InlineData(-73)]
    public void Rotated_text_width_resize_scales_local_font_and_wrap_together_and_keeps_canvas_anchor(double rotation)
    {
        var artifact=AnnotationRenderingTests.Font();var font=new FontSpec(artifact.Id,20,20);
        var text=new TextGeometry(new(100,100),"A",100,rotation);var layer=new Annotation(Guid.NewGuid(),true,text,Ink with {Font=font});
        var local=ImageLayerTextMetrics.Measure(artifact,font,text);var before=ImageLayerGeometry.Bounds(layer,local);
        var resized=ImageLayerGeometry.SetBounds(layer,before with {Width=before.Width*2},local);
        var afterLocal=ImageLayerTextMetrics.Measure(artifact,resized.Style.Font!,(TextGeometry)resized.Geometry);var after=ImageLayerGeometry.Bounds(resized,afterLocal);
        Assert.Equal(before.X,after.X,5);Assert.Equal(before.Y,after.Y,5);
        Assert.Equal(before.Width*2,after.Width,5);Assert.Equal(before.Height*2,after.Height,5);
        Assert.Equal(40,resized.Style.Font!.SizePixels);Assert.Equal(200,((TextGeometry)resized.Geometry).WrapWidth);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Rotated_text_corner_resize_keeps_its_opposite_canvas_corner(int handle)
    {
        var artifact=AnnotationRenderingTests.Font();var font=new FontSpec(artifact.Id,20,20);
        var layer=new Annotation(Guid.NewGuid(),true,new TextGeometry(new(200,200),"AAAA",100,45),Ink with {Font=font});
        var local=ImageLayerTextMetrics.Measure(artifact,font,(TextGeometry)layer.Geometry);var before=ImageLayerGeometry.Bounds(layer,local);
        var target=ImageLayerGeometry.DragTextBounds(before,handle,handle is 0 or 3?-30:30,handle is 0 or 1?-10:10);
        var resized=ImageLayerGeometry.SetBounds(layer,target,local);var actual=ImageLayerGeometry.Bounds(resized,ImageLayerTextMetrics.Measure(artifact,resized.Style.Font!,(TextGeometry)resized.Geometry));
        var anchor=ImageLayerGeometry.Corners(before)[(handle+2)%4];var afterAnchor=ImageLayerGeometry.Corners(actual)[(handle+2)%4];
        Assert.Equal(anchor.X,afterAnchor.X,4);Assert.Equal(anchor.Y,afterAnchor.Y,4);
    }
    private static ImageRecipe Recipe()=>new(Guid.NewGuid(),new(0,false,false,null,null,false),new(1,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,1,PhotoEffect.None,1),[],null,[],[]);
}
