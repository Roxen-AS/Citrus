using System.Collections.Immutable;
using System.Security.Cryptography;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Image;

namespace Citrus.Conversion.Tests;

public sealed class AnnotationRenderingTests
{
    internal static FontArtifact Font(int number = 1, int faceIndex = 0, bool collection = false)
    {
        string path = Path.Combine(AppContext.BaseDirectory,"Fixtures","Fonts",collection ? "FixtureFaces.ttc" : $"Fixture{number}.ttf");
        return new(Guid.NewGuid(),path,new FileInfo(path).Length,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),faceIndex,$"Citrus Fixture {number}","Regular");
    }
    private static PreviewRequest Request(SizeI? size = null, RectD? tile = null) => new(7,size ?? new(2048,2048),null,null,tile);

    [Fact]
    public async Task Saved_shapes_strokes_arrows_and_highlights_change_actual_pixels()
    {
        using var f = new ImageLayerFixture(); var doc = await f.Create();
        var style = ImageLayerFixture.Red;
        doc = doc with { Recipe = ((ImageRecipe)doc.Recipe) with { Annotations = [
            new(Guid.NewGuid(),true,new ShapeGeometry(new(5,5,15,10),false,0),style),
            new(Guid.NewGuid(),true,new ShapeGeometry(new(30,5,15,10),true,0),style),
            new(Guid.NewGuid(),true,new StrokeGeometry([new(5,30),new(15,35),new(25,30)],true,true),style with {StrokeWidth = 3}),
            new(Guid.NewGuid(),true,new ShapeGeometry(new(50,5,15,10),false,3),style with {Opacity = .5})] } };
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService());
        var actual = await renderer.RenderPreviewAsync(doc,Request(),default);
        foreach (var (x,y) in new[] {(10,10),(37,10),(15,34)}) Assert.Equal(new byte[]{0,0,255,255},ImageLayerFixture.Pixel(actual,x,y));
        Assert.InRange(ImageLayerFixture.Pixel(actual,55,10)[0],(byte)126,(byte)129);
        Assert.Equal(new byte[]{255,255,255,255},ImageLayerFixture.Pixel(actual,75,55));
        string stage = f.PathIn("export"); var result = await renderer.RenderExportAsync(doc,stage,null,default);
        Assert.Equal(actual.Bgra32.ToArray(),ImageLayerFixture.Pixels(Path.Combine(stage,"payload",result.Files[0].RelativePath)));
    }

    [Fact]
    public async Task Same_explicit_font_layout_in_preview_export_and_source_tile()
    {
        using var f = new ImageLayerFixture(); var doc = await f.Create(); var font = Font();
        doc = doc with { Fonts = [font], Recipe = ((ImageRecipe)doc.Recipe) with { Annotations = [new(Guid.NewGuid(),true,new TextGeometry(new(10,10),"AB中文",25,0),ImageLayerFixture.Red with {Font = new(font.Id,12,15)})] } };
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService()); var actual = await renderer.RenderPreviewAsync(doc,Request(),default);
        Assert.Equal(new byte[] {0,0,255,255},ImageLayerFixture.Pixel(actual,12,15));
        Assert.Equal(new byte[] {0,0,255,255},ImageLayerFixture.Pixel(actual,12,30));
        string stage = f.PathIn("export"); var result = await renderer.RenderExportAsync(doc,stage,null,default);
        Assert.Equal(actual.Bgra32.ToArray(),ImageLayerFixture.Pixels(Path.Combine(stage,"payload",result.Files[0].RelativePath)));
        var detail = await renderer.RenderPreviewAsync(doc,Request(tile:new(10,10,30,30)),default);
        for (int y=0;y<30;y++) for(int x=0;x<30;x++) Assert.Equal(ImageLayerFixture.Pixel(actual,x+10,y+10),ImageLayerFixture.Pixel(detail,x,y));
    }

    [Fact]
    public async Task Magnifier_samples_masked_pre_annotation_image_and_inset_uses_verified_auxiliary()
    {
        using var f = new ImageLayerFixture(); var doc = await f.Create(); var aux = await f.Create(10,10);
        var recipe = (ImageRecipe)doc.Recipe;
        doc = doc with { Sources = doc.Sources.Add(aux.Sources[0]), Recipe = recipe with { Masks = [new(Guid.NewGuid(),new(5,5,10,10),MaskEffect.Solid,new(0,0,0,255),0)], Annotations = [
            new(Guid.NewGuid(),true,new ShapeGeometry(new(5,5,10,10),false,0),ImageLayerFixture.Red),
            new(Guid.NewGuid(),true,new MagnifierGeometry(new(5,5,10,10),new(30,5,20,20),1),ImageLayerFixture.Red),
            new(Guid.NewGuid(),true,new InsetImageGeometry(aux.Sources[0].Id,new(60,5,10,10)),ImageLayerFixture.Red)]} };
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService()); var frame = await renderer.RenderPreviewAsync(doc,Request(),default);
        Assert.Equal(new byte[]{0,0,255,255},ImageLayerFixture.Pixel(frame,10,10));
        Assert.Equal(new byte[]{0,0,0,255},ImageLayerFixture.Pixel(frame,40,15));
        Assert.Equal(new byte[]{255,255,255,255},ImageLayerFixture.Pixel(frame,65,10));
    }

    [Fact]
    public async Task Changed_font_is_rejected_even_after_preview_cached_it()
    {
        using var f = new ImageLayerFixture(); var font = Font(); string local = f.PathIn("font.ttf"); File.Copy(font.OriginalPath,local); font = font with {OriginalPath=local};
        var doc = await f.Create(); doc = doc with { Fonts=[font],Recipe=((ImageRecipe)doc.Recipe) with {Annotations=[new(Guid.NewGuid(),true,new TextGeometry(new(5,5),"ABC",60,0),ImageLayerFixture.Red with {Font=new(font.Id,12,15)})]} };
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService()); await renderer.RenderPreviewAsync(doc,Request(),default);
        byte[] bytes=File.ReadAllBytes(local); bytes[^1]^=1; File.WriteAllBytes(local,bytes);
        await Assert.ThrowsAsync<IOException>(()=>renderer.RenderPreviewAsync(doc,Request(),default));
    }

    [Fact]
    public async Task Scaled_viewports_use_saved_canvas_coordinates_and_stroke_widths()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create(1600,800);
        doc=doc with {Recipe=((ImageRecipe)doc.Recipe) with {Annotations=[new(Guid.NewGuid(),true,new ShapeGeometry(new(400,200,200,200),false,0),ImageLayerFixture.Red)],Masks=[new(Guid.NewGuid(),new(800,200,200,200),MaskEffect.Solid,new(0,0,0,255),0)]}};
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());
        foreach(var frame in new[]{await renderer.RenderPreviewAsync(doc,Request(new(1200,1200)),default),await renderer.RenderRefinedPreviewAsync(doc,Request(new(1200,1200)),default)})
        {
            Assert.Contains(frame.Size.Width,new[]{512,1024});double scale=frame.Size.Width/1600d;
            Assert.Equal(new byte[]{0,0,255,255},ImageLayerFixture.Pixel(frame,(int)(500*scale),(int)(300*scale)));
            Assert.Equal(new byte[]{0,0,0,255},ImageLayerFixture.Pixel(frame,(int)(900*scale),(int)(300*scale)));
            Assert.Equal(new byte[]{255,255,255,255},ImageLayerFixture.Pixel(frame,(int)(1100*scale),(int)(300*scale)));
            Assert.Contains(frame.Warnings,w=>w.Contains("Scaled preview",StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Delete_callout_preserves_remaining_number_and_hidden_layers_do_not_paint()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();var font=Font();
        var style=ImageLayerFixture.Red with {Fill=new(0,0,0,255),Stroke=new(255,255,255,255),Font=new(font.Id,12,15)};
        var first=new Annotation(Guid.NewGuid(),true,new NumberGeometry(new(5,5,20,20),3),style);
        var second=new Annotation(Guid.NewGuid(),true,new NumberGeometry(new(40,5,20,20),8),style);
        var recipe=((ImageRecipe)doc.Recipe) with {Annotations=[first,second]};doc=doc with {Fonts=[font],Recipe=recipe};
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());var before=await renderer.RenderPreviewAsync(doc,Request(),default);
        var after=await renderer.RenderPreviewAsync(doc with {Recipe=recipe with {Annotations=[second]}},Request(),default);
        for(int y=5;y<25;y++)for(int x=40;x<60;x++)Assert.Equal(ImageLayerFixture.Pixel(before,x,y),ImageLayerFixture.Pixel(after,x,y));
        Assert.Equal(8,((NumberGeometry)second.Geometry).Number);
        Assert.Equal(new byte[]{255,255,255,255},ImageLayerFixture.Pixel(after,10,10));
        var hidden=await renderer.RenderPreviewAsync(doc with {Recipe=recipe with {Annotations=[first with {Visible=false}]}},Request(),default);
        Assert.All(hidden.Bgra32,value=>Assert.Equal(255,value));
    }

    [Fact]
    public async Task Distinct_faces_render_their_own_advances_and_preview_fonts_are_cleaned()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();var first=Font(1);var second=Font(2,1,true);
        doc=doc with {Fonts=[first,second],Recipe=((ImageRecipe)doc.Recipe) with {Annotations=[
            new(Guid.NewGuid(),true,new TextGeometry(new(5,5),"AA",60,0),ImageLayerFixture.Red with {Font=new(first.Id,20,24)}),
            new(Guid.NewGuid(),true,new TextGeometry(new(5,30),"AA",60,0),ImageLayerFixture.Red with {Font=new(second.Id,20,24)})]}};
        string cache=f.PathIn("cache");using(var renderer=new ImageRecipeRenderer(new SourceSnapshotService(),cache))
        {
            var frame=await renderer.RenderPreviewAsync(doc,Request(),default);
            Assert.Equal(new byte[]{255,255,255,255},ImageLayerFixture.Pixel(frame,29,10));
            Assert.Equal(new byte[]{0,0,255,255},ImageLayerFixture.Pixel(frame,29,35));
            Assert.Empty(Directory.EnumerateFiles(cache,"*.font",SearchOption.AllDirectories));
        }
        Assert.False(Directory.Exists(cache));
    }

    [Fact]
    public async Task Standalone_annotation_layer_is_transparent_and_verifies_supplied_source_bytes()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();
        doc=doc with {Recipe=((ImageRecipe)doc.Recipe) with {Annotations=[new(Guid.NewGuid(),true,new ShapeGeometry(new(10,10,10,10),false,0),ImageLayerFixture.Red)]}};
        var paths=await new SourceSnapshotService().StageVerifiedAsync(doc.Sources,f.PathIn("sources"),default);
        var rasterizer=new AnnotationRasterizer();var frame=rasterizer.Render(doc,Request(),paths);
        Assert.Equal(new byte[]{0,0,0,0},ImageLayerFixture.Pixel(frame,0,0));Assert.Equal(new byte[]{0,0,255,255},ImageLayerFixture.Pixel(frame,15,15));
        File.WriteAllBytes(paths[doc.Sources[0].Id],[1,2,3]);Assert.Throws<IOException>(()=>rasterizer.Render(doc,Request(),paths));
    }

    [Fact]
    public async Task Admission_rejects_unsupported_text_and_auxiliary_is_never_a_root()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();var aux=await f.Create();var font=Font();
        doc=doc with {Fonts=[font],Sources=doc.Sources.Add(aux.Sources[0]),Recipe=((ImageRecipe)doc.Recipe) with {Annotations=[
            new(Guid.NewGuid(),true,new InsetImageGeometry(aux.Sources[0].Id,new(0,0,5,5)),ImageLayerFixture.Red),
            new(Guid.NewGuid(),true,new TextGeometry(new(10,10),"Q",40,0),ImageLayerFixture.Red with {Font=new(font.Id,12,15)})]}};
        var request=new OperationRequest(new("image.crop","Crop",OperationMode.Tools,null,false,true),[doc.Sources[0].Path],new(),null,doc);
        var errors=await new ImageEditorAdmissionInspector(new SourceSnapshotService()).ValidateAsync(request,default);
        Assert.Contains(errors,e=>e.Message.Contains("no glyph",StringComparison.Ordinal));Assert.Single(request.Inputs);
    }

    [Fact]
    public async Task Cancellation_after_source_staging_removes_all_owned_font_and_source_files()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();var font=Font();
        doc=doc with {Fonts=[font],Recipe=((ImageRecipe)doc.Recipe) with {Annotations=[new(Guid.NewGuid(),true,new TextGeometry(new(5,5),"ABC",60,0),ImageLayerFixture.Red with {Font=new(font.Id,12,15)})]}};
        string stage=f.PathIn("stage");using var cancel=new CancellationTokenSource();using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>renderer.RenderExportAsync(doc,stage,new CancelProgress(cancel),cancel.Token));
        Assert.False(Directory.Exists(stage));Assert.True(File.Exists(doc.Sources[0].Path));Assert.True(File.Exists(font.OriginalPath));
    }
    private sealed class CancelProgress(CancellationTokenSource cancellation):IProgress<ConversionProgress>
    { public void Report(ConversionProgress value)=>cancellation.Cancel(); }

    [Fact]
    public async Task Text_outline_uses_saved_stroke_color_and_width()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();var font=Font();
        var style=ImageLayerFixture.Red with {Fill=new(0,0,0,0),StrokeWidth=2,Font=new(font.Id,20,24)};
        doc=doc with {Fonts=[font],Recipe=((ImageRecipe)doc.Recipe) with {Annotations=[new(Guid.NewGuid(),true,new TextGeometry(new(10,10),"A",60,0),style)]}};
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());var frame=await renderer.RenderPreviewAsync(doc,new(1,new(80,60),null,null,null),default);
        Assert.Equal(new byte[]{0,0,255,255},ImageLayerFixture.Pixel(frame,11,15));
        Assert.Equal(new byte[]{255,255,255,255},ImageLayerFixture.Pixel(frame,15,18));
    }
}
