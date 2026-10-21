using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Image;
using ImageMagick;

namespace Citrus.Conversion.Tests;

public sealed class ImageMaskTests
{
    [Fact]
    public async Task Solid_mask_replaces_original_pixels_before_geometry_and_export_matches_detail()
    {
        using var f = new ImageLayerFixture(); var doc = await f.Create();
        doc = doc with { Recipe = ((ImageRecipe)doc.Recipe) with { Geometry = new(1,true,false,null,null,false), Masks = [new(Guid.NewGuid(),new(10,20,12,8),MaskEffect.Solid,new(0,0,0,255),0)] } };
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService());
        var preview = await renderer.RenderPreviewAsync(doc,new(1,new(2048,2048),null,null,null),default);
        Assert.Equal(new byte[] {0,0,0,255}, ImageLayerFixture.Pixel(preview,35,65));
        Assert.Equal(new byte[] {255,255,255,255}, ImageLayerFixture.Pixel(preview,5,5));
        string stage = f.PathIn("export"); var exported = await renderer.RenderExportAsync(doc,stage,null,default);
        Assert.Equal(preview.Bgra32.ToArray(),ImageLayerFixture.Pixels(Path.Combine(stage,"payload",exported.Files[0].RelativePath)));
        var tile = await renderer.RenderPreviewAsync(doc,new(2,new(2048,2048),null,null,new(10,20,12,8)),default);
        Assert.All(tile.Bgra32.Chunk(4), pixel => Assert.Equal(new byte[] {0,0,0,255},pixel));
    }

    [Fact]
    public async Task Solid_mask_discards_embedded_profiles_even_with_preserve_metadata()
    {
        using var f = new ImageLayerFixture(); var doc = await f.Create();
        using (var image = new MagickImage(doc.Sources[0].Path)) { image.SetProfile(ThumbnailProfile()); image.Comment = "original payload"; image.Write(doc.Sources[0].Path); }
        using (var input = new MagickImage(doc.Sources[0].Path)) Assert.True(input.GetExifProfile()!.ThumbnailLength > 0);
        var source = (await new SourceSnapshotService().CaptureAsync([doc.Sources[0].Path],default))[0].Snapshot!;
        doc = doc with { Sources = [source], Recipe = ((ImageRecipe)doc.Recipe) with {SourceId = source.Id, Masks = [new(Guid.NewGuid(),new(0,0,80,60),MaskEffect.Solid,new(0,0,0,255),0)]} };
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService()); string stage = f.PathIn("export");
        var output = await renderer.RenderExportAsync(doc,stage,null,default);
        using var actual = new MagickImage(Path.Combine(stage,"payload",output.Files[0].RelativePath));
        Assert.Null(actual.GetExifProfile()); Assert.True(string.IsNullOrEmpty(actual.Comment)); Assert.Equal(new[] {"icc"},actual.ProfileNames);
    }

    [Theory]
    [InlineData(MaskEffect.Gaussian)]
    [InlineData(MaskEffect.Pixelate)]
    public async Task Gaussian_and_pixelate_warn_that_they_are_visual_obscuring(MaskEffect effect)
    {
        using var f = new ImageLayerFixture(); var doc = await f.Create();
        doc = doc with { Recipe = ((ImageRecipe)doc.Recipe) with { Masks = [new(Guid.NewGuid(),new(0,0,20,20),effect,new(0,0,0,255),4)] } };
        using var renderer = new ImageRecipeRenderer(new SourceSnapshotService());
        var frame = await renderer.RenderPreviewAsync(doc,new(1,new(80,60),null,null,null),default);
        Assert.Contains(frame.Warnings,s => s.Contains("visual obscuring",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MaskEffect.Gaussian)]
    [InlineData(MaskEffect.Pixelate)]
    public async Task Obscuring_changes_high_frequency_pixels_only_inside_saved_region(MaskEffect effect)
    {
        using var f=new ImageLayerFixture();var doc=await f.Create(24,16);string sourcePath=doc.Sources[0].Path;
        byte[] bytes=new byte[24*16*4];for(int y=0;y<16;y++)for(int x=0;x<24;x++){int i=(y*24+x)*4;bytes[i]=bytes[i+1]=bytes[i+2]=(byte)(((x+y)%2)*255);bytes[i+3]=255;}
        using(var image=new MagickImage(bytes,new PixelReadSettings(24,16,StorageType.Char,PixelMapping.RGBA)))image.Write(sourcePath);
        var source=(await new SourceSnapshotService().CaptureAsync([sourcePath],default))[0].Snapshot!;
        doc=doc with {Sources=[source],Recipe=((ImageRecipe)doc.Recipe) with {SourceId=source.Id,Masks=[new(Guid.NewGuid(),new(4,4,12,8),effect,new(0,0,0,255),4)]}};
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());var frame=await renderer.RenderPreviewAsync(doc,new(1,new(24,16),null,null,null),default);
        Assert.InRange(ImageLayerFixture.Pixel(frame,8,8)[0],(byte)110,(byte)145);
        Assert.Equal(new byte[]{0,0,0,255},ImageLayerFixture.Pixel(frame,0,0));
        Assert.Equal(new byte[]{255,255,255,255},ImageLayerFixture.Pixel(frame,1,0));
    }

    [Fact]
    public async Task Transparent_solid_mask_replaces_hidden_rgb_instead_of_alpha_compositing()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();
        doc=doc with {Recipe=((ImageRecipe)doc.Recipe) with {Masks=[new(Guid.NewGuid(),new(0,0,80,60),MaskEffect.Solid,new(0,0,0,0),0)]}};
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());var frame=await renderer.RenderPreviewAsync(doc,new(1,new(80,60),null,null,null),default);
        Assert.All(frame.Bgra32,b=>Assert.Equal(0,b));
    }

    [Fact]
    public async Task Publication_validator_rejects_reintroduced_original_thumbnail_after_solid_mask()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();
        doc=doc with {Recipe=((ImageRecipe)doc.Recipe) with {Masks=[new(Guid.NewGuid(),new(0,0,20,20),MaskEffect.Solid,new(0,0,0,255),0)]}};
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());string stage=f.PathIn("stage");var output=await renderer.RenderExportAsync(doc,stage,null,default);
        var request=new OperationRequest(new("image.crop","Crop",OperationMode.Tools,null,false,true),[doc.Sources[0].Path],new(),null,doc);
        var validator=new ImageEditorPayloadValidator();await validator.ValidateAsync(request,output,stage,default);
        string payload=Path.Combine(stage,"payload",output.Files[0].RelativePath);
        using(var image=new MagickImage(payload)){image.SetProfile(ThumbnailProfile());image.Write(payload);}
        output=output with {Files=[output.Files[0] with {Bytes=new FileInfo(payload).Length}]};
        await Assert.ThrowsAsync<InvalidDataException>(()=>validator.ValidateAsync(request,output,stage,default));
    }

    private static ExifProfile ThumbnailProfile()
    {
        using var thumbnail=new MagickImage(MagickColors.Red,8,6);byte[] jpeg=thumbnail.ToByteArray(MagickFormat.Jpeg);
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(new byte[]{69,120,105,102,0,0,73,73});writer.Write((ushort)42);writer.Write((uint)8);
        writer.Write((ushort)0);writer.Write((uint)14); // empty IFD0, pointer to IFD1
        writer.Write((ushort)2);
        writer.Write((ushort)0x0201);writer.Write((ushort)4);writer.Write((uint)1);writer.Write((uint)44);
        writer.Write((ushort)0x0202);writer.Write((ushort)4);writer.Write((uint)1);writer.Write((uint)jpeg.Length);
        writer.Write((uint)0);writer.Write(jpeg);
        return new ExifProfile(stream.ToArray());
    }

    [Theory]
    [InlineData(MaskEffect.Gaussian)]
    [InlineData(MaskEffect.Pixelate)]
    public async Task Visual_masks_preserve_source_alpha_inside_and_outside_region(MaskEffect effect)
    {
        using var f=new ImageLayerFixture();var doc=await f.Create(24,16);string path=doc.Sources[0].Path;
        using(var image=new MagickImage(new MagickColor(100,120,140,96),24,16))image.Write(path);
        var source=(await new SourceSnapshotService().CaptureAsync([path],default))[0].Snapshot!;
        doc=doc with {Sources=[source],Recipe=((ImageRecipe)doc.Recipe) with {SourceId=source.Id,Masks=[new(Guid.NewGuid(),new(4,4,12,8),effect,new(0,0,0,255),4)]}};
        using var renderer=new ImageRecipeRenderer(new SourceSnapshotService());var frame=await renderer.RenderPreviewAsync(doc,new(1,new(24,16),null,null,null),default);
        Assert.Equal(96,ImageLayerFixture.Pixel(frame,8,8)[3]);Assert.Equal(96,ImageLayerFixture.Pixel(frame,0,0)[3]);
    }
}
