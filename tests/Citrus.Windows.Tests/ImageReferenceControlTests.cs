using Citrus.Core.Editing;
using Citrus.Windows.Editors;
using Citrus.Windows.Editors.Image;

namespace Citrus.Windows.Tests;

public sealed class ImageReferenceControlTests
{
    private static readonly ImageAdjustments Identity = new(1,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,1,PhotoEffect.None,1);
    [Fact]
    public void Light_controls_include_whites_blacks_and_signed_neutral_gamma()
    {
        var fields=ImageEditorViewModel.Fields;
        Assert.Contains(fields,f=>f.Property=="Whites"); Assert.Contains(fields,f=>f.Property=="Blacks");
        var gamma=Assert.Single(fields,f=>f.Property=="Gamma");
        Assert.Equal(0,gamma.Read(Identity)); Assert.Equal(1.5,gamma.Write(Identity,.5).Gamma);
        Assert.Contains(fields,f=>f.Label=="Temperature");
    }
    [Theory]
    [InlineData(4,0,-5,10,5,40,25)]
    [InlineData(5,5,0,10,10,45,20)]
    [InlineData(6,0,5,10,10,40,25)]
    [InlineData(7,-5,0,5,10,45,20)]
    public void Edge_handles_resize_only_the_selected_axis(int handle,double dx,double dy,double x,double y,double width,double height)
    {
        Assert.Equal(new RectD(x,y,width,height),CropGestureGeometry.Drag(new(10,10,40,20),new(100,80),handle,dx,dy,null));
    }
    [Fact]
    public void Image_crop_hit_test_includes_edge_midpoints()
    {
        var r=new System.Windows.Rect(20,20,100,60);
        Assert.Equal(4,CropOverlay.HitHandle(r,new(70,20),false)); Assert.Equal(5,CropOverlay.HitHandle(r,new(120,50),false));
        Assert.Equal(6,CropOverlay.HitHandle(r,new(70,80),false)); Assert.Equal(7,CropOverlay.HitHandle(r,new(20,50),false));
    }
    [Fact]
    public void Ratio_edge_drag_can_expand_around_the_opposite_edge_without_leaving_the_image()
    {
        var resized=CropGestureGeometry.Drag(new(20,20,40,20),new(100,80),4,0,-5,2);
        Assert.Equal(new RectD(15,15,50,25),resized);
    }
    [Fact]
    public void Native_detail_with_fractional_background_placement_stays_inside_source_and_uses_whole_pixels()
    {
        var recipe=new ImageRecipe(Guid.NewGuid(),new(0,false,false,null,null,false),Identity,[],
            new(new(130,90),new SolidBackground(new(255,255,255,255)),new(10.5,20.5,100,50),ImageFit.Stretch,0,null),[],[]);
        var request=ImageDetailPreview.Create(recipe,new(100,50),1,new(512,512));
        var tile=Assert.IsType<RectD>(request.SourceTile);
        Assert.True(tile.X>=0&&tile.Y>=0&&tile.X+tile.Width<=100&&tile.Y+tile.Height<=50);
        var first=Citrus.Conversion.Editing.Image.ImageGeometryMapper.MapToCanvas(recipe,new(100,50),new(tile.X,tile.Y));
        var last=Citrus.Conversion.Editing.Image.ImageGeometryMapper.MapToCanvas(recipe,new(100,50),new(tile.X+tile.Width,tile.Y+tile.Height));
        Assert.Equal(99,Math.Ceiling(last.X)-Math.Floor(first.X));
        Assert.Equal(49,Math.Ceiling(last.Y)-Math.Floor(first.Y));
    }
}
