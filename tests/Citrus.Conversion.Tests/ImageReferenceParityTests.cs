using Citrus.Core.Editing;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Image;
using ImageMagick;

namespace Citrus.Conversion.Tests;

public sealed class ImageReferenceParityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "citrus-reference-image-" + Guid.NewGuid().ToString("N"));
    private static readonly ImageAdjustments Identity = new(1,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,1,PhotoEffect.None,0);
    public ImageReferenceParityTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private async Task<EditorDocument> Document()
    {
        string path = Path.Combine(root,"source.png");
        using(var image = new MagickImage(MagickColors.Red, 40, 24)) await image.WriteAsync(path);
        var source = (await new SourceSnapshotService().CaptureAsync([path],default))[0].Snapshot!;
        return new(1,0,[source],[],new ImageRecipe(source.Id,new(0,false,false,null,null,false),Identity,[],null,[],[]),new("png",root,"copy",new QualityGoal(85),MetadataPolicy.PreserveSupported,new(255,255,255,255)));
    }
    [Fact]
    public void Whites_and_blacks_are_neutral_by_default_and_independently_addressable()
    {
        foreach(string name in new[]{"Whites","Blacks"})
        {
            var property = typeof(ImageAdjustments).GetProperty(name);
            Assert.NotNull(property); Assert.Equal(0d, property.GetValue(Identity));
        }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Endpoint_tone_controls_change_their_intended_pixels_and_preserve_alpha(bool whites)
    {
        byte[] source=[32,32,32,90,220,220,220,180];
        using var image=new MagickImage(source,new PixelReadSettings(2,1,StorageType.Char,PixelMapping.RGBA));
        var adjustments=whites?Identity with{Whites=.8}:Identity with{Blacks=.8};
        ImageAdjustmentsPipeline.Apply(image,new(Guid.NewGuid(),new(0,false,false,null,null,false),adjustments,[],null,[],[]),new(2,1),default);
        byte[] result=image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
        Assert.Equal(source[3],result[3]);Assert.Equal(source[7],result[7]);
        Assert.True(whites?result[4]-source[4]>result[0]-source[0]:result[0]-source[0]>result[4]-source[4]);
    }
    [Fact]
    public void Background_changes_remap_annotations_and_reset_round_trips_without_changing_masks()
    {
        Guid source=Guid.NewGuid();var mask=new PixelMask(Guid.NewGuid(),new(1,1,2,2),MaskEffect.Solid,new(0,0,0,255),0);
        var annotation=new Annotation(Guid.NewGuid(),true,new ShapeGeometry(new(4,3,10,5),false,2),new(new(1,2,3,255),new(0,0,0,0),2,1,null));
        var doc=new EditorDocument(1,0,[],[],new ImageRecipe(source,new(0,false,false,null,null,false),Identity,[mask],null,[annotation],[]),new("png",null,"test",new QualityGoal(85),MetadataPolicy.PreserveSupported,new(255,255,255,255)));
        var canvas=new CanvasSpec(new(100,80),new SolidBackground(new(0,0,255,255)),new(20,20,60,40),ImageFit.Stretch,4,null);
        var changed=ImageCanvasPlacement.Change(doc,new(30,20),canvas);var recipe=(ImageRecipe)changed.Recipe;
        Assert.Equal(new RectD(28,26,20,10),((ShapeGeometry)recipe.Annotations[0].Geometry).Bounds);Assert.Equal(mask,recipe.Masks[0]);
        var restored=ImageCanvasPlacement.Change(changed,new(30,20),null);Assert.Equal(annotation,((ImageRecipe)restored.Recipe).Annotations[0]);
    }
    [Fact]
    public async Task Crop_context_drops_unused_background_sources_and_reanchors_canvas_annotations_without_mutating_draft()
    {
        var doc=await Document();string auxiliaryPath=Path.Combine(root,"auxiliary.png");File.Copy(doc.Sources[0].Path,auxiliaryPath);
        var auxiliary=(await new SourceSnapshotService().CaptureAsync([auxiliaryPath],default))[0].Snapshot!;
        var recipe=(ImageRecipe)doc.Recipe;
        var annotation=new Annotation(Guid.NewGuid(),true,new ShapeGeometry(new(26,26,12,9),false,0),new(new(0,0,0,255),new(0,0,0,0),1,1,null));
        doc=doc with{Sources=doc.Sources.Add(auxiliary),Recipe=recipe with{Geometry=recipe.Geometry with{Crop=new(10,5,20,10)},Canvas=new(new(100,80),new ImageBackground(auxiliary.Id,ImageFit.Cover,new(.5,.5),1,0),new(20,20,60,30),ImageFit.Contain,0,null),Annotations=[annotation]}};
        var contextType=Type.GetType("Citrus.Conversion.Editing.Image.ImageCropContext, Citrus.Conversion");Assert.NotNull(contextType);
        var context=(EditorDocument)contextType.GetMethod("Create")!.Invoke(null,[doc,new SizeI(40,24)])!;
        Assert.Single(context.Sources);Assert.Equal(doc.Sources[0],context.Sources[0]);var contextRecipe=(ImageRecipe)context.Recipe;
        Assert.Null(contextRecipe.Canvas);Assert.Null(contextRecipe.Geometry.Crop);Assert.Null(contextRecipe.Geometry.OutputSize);
        Assert.Equal(new RectD(12,7,4,3),((ShapeGeometry)contextRecipe.Annotations[0].Geometry).Bounds);
        Assert.Equal(2,doc.Sources.Length);Assert.NotNull(((ImageRecipe)doc.Recipe).Canvas);
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());var preview=await renderer.RenderPreviewAsync(context,new(1,new(512,512),null,null,null),default);Assert.Equal(new SizeI(40,24),preview.Size);
    }
    [Fact]
    public async Task Failed_HEIC_preparation_releases_the_gate_for_the_next_attempt()
    {
        var method=typeof(HeicCodec).GetMethod("RunPreparedAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);Assert.NotNull(method);
        var failure=(Task)method.Invoke(null,[new Func<Task>(()=>Task.FromException(new IOException("preparation cleanup failed"))),CancellationToken.None])!;
        await Assert.ThrowsAsync<IOException>(()=>failure);
        bool ran=false;var next=(Task)method.Invoke(null,[new Func<Task>(()=>{ran=true;return Task.CompletedTask;}),CancellationToken.None])!;
        await next.WaitAsync(TimeSpan.FromSeconds(2));Assert.True(ran);
    }
    [Fact]
    public async Task Background_canvas_exports_real_padding_and_preserves_preview_pixels()
    {
        var doc = await Document();
        doc = doc with {Recipe = ((ImageRecipe)doc.Recipe) with {Canvas = new(new(60,44),new SolidBackground(new(0,0,255,255)),new(10,10,40,24),ImageFit.Contain,0,null)}};
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService());
        var preview = await renderer.RenderPreviewAsync(doc,new(1,new(2048,2048),null,null,null),default);
        var export = await renderer.RenderExportAsync(doc,Path.Combine(root,"stage"),null,default);
        using var output = new MagickImage(Path.Combine(root,"stage","payload",export.Files.Single().RelativePath));
        Assert.Equal(new SizeI(60,44),preview.Size);
        Assert.Equal(preview.Bgra32.ToArray(),output.GetPixels().ToByteArray(PixelMapping.BGRA));
        var pixels = output.GetPixels().ToByteArray(PixelMapping.RGBA)!;
        Assert.Equal(new byte[]{0,0,255,255},pixels.Take(4));
        Assert.Equal(new byte[]{255,0,0,255},pixels.Skip((11*60+11)*4).Take(4));
    }
    [Fact]
    public async Task Byte_goal_is_measured_after_export_and_does_not_resize()
    {
        var doc = await Document(); doc = doc with {Export=doc.Export with {Compression=new ByteGoal(8192)}};
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService());
        var result = await renderer.RenderExportAsync(doc,Path.Combine(root,"stage"),null,default);
        string path = Path.Combine(root,"stage","payload",result.Files.Single().RelativePath);
        Assert.InRange(new FileInfo(path).Length,1,8192); Assert.Equal(8192,result.Compression.TargetBytes); Assert.True(result.Compression.MetTarget);
        using var output = new MagickImage(path); Assert.Equal(40u,output.Width); Assert.Equal(24u,output.Height);
    }
    [Fact]
    public async Task Unreachable_byte_goal_does_not_publish_a_larger_candidate()
    {
        var doc=await Document();doc=doc with{Export=doc.Export with{Compression=new ByteGoal(1)}};
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());string stage=Path.Combine(root,"unreachable");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>renderer.RenderExportAsync(doc,stage,null,default));
        Assert.False(Directory.Exists(Path.Combine(stage,"payload")));
    }
    [Fact]
    public async Task Native_HEIC_probe_reports_real_HEVC_encoder_availability()
    {
        await HeicCodec.VerifyAsync(default);
        var doc=await Document();var engine=new ImageEngine();
        var result=await engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id=="image.convert.heic"),[doc.Sources[0].Path],new(),root),null,default);
        string path=result.Outputs.Single();
        byte[] header=File.ReadAllBytes(path);
        Assert.Contains("hvc",System.Text.Encoding.ASCII.GetString(header));
        var restored=await engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id=="image.convert.png"),[path],new(),root),null,default);
        using var decoded=new MagickImage(restored.Outputs.Single()); Assert.Equal(40u,decoded.Width); Assert.Equal(24u,decoded.Height);
    }
    [Fact]
    public async Task HEIC_transparency_survives_encoding_without_changing_alpha_samples()
    {
        await HeicCodec.VerifyAsync(default);byte[] pixels=new byte[9*7*4];
        for(int n=0;n<63;n++){pixels[n*4]=255;pixels[n*4+1]=85;pixels[n*4+3]=(byte)(n*37%256);}
        using var image=new MagickImage(pixels,new PixelReadSettings(9,7,StorageType.Char,PixelMapping.RGBA));string path=Path.Combine(root,"alpha.heic");
        await HeicCodec.WriteAsync(image,path,85,default);using var decoded=HeicCodec.Load(path,default);
        Assert.Equal(pixels.Where((_,i)=>i%4==3),decoded.GetPixels().ToByteArray(PixelMapping.RGBA)!.Where((_,i)=>i%4==3));
    }
}
