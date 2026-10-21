using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Conversion.Editing;
using ImageMagick;

namespace Citrus.Conversion.Tests;

public sealed class ImageMetadataReferenceTests
{
    [Fact]
    public async Task Metadata_editor_reads_and_saves_a_real_tag_without_reencoding_source_pixels()
    {
        var serviceType=Type.GetType("Citrus.Conversion.Editing.Image.ExifToolMetadataService, Citrus.Conversion");
        var engineType=Type.GetType("Citrus.Conversion.Editing.Image.ImageMetadataEngine, Citrus.Conversion");
        Assert.NotNull(serviceType);Assert.NotNull(engineType);
        string root=Path.Combine(Path.GetTempPath(),"citrus-metadata-照片-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string input=Path.Combine(root,"source.png");
            using(var image=new MagickImage(MagickColors.Orange,13,9)){var exif=new ExifProfile();exif.SetValue(ExifTag.Artist,"Before");image.SetProfile(exif);image.Write(input);}
            var source=(await new SourceSnapshotService().CaptureAsync([input],default))[0].Snapshot!;
            var service=(IMetadataService)Activator.CreateInstance(serviceType,FindTool())!;
            var fields=await service.ReadAsync(source,default);var comment=Assert.Single(fields,f=>f.Key=="Artist");Assert.True(comment.Writable);
            Assert.Contains(fields,f=>f.Scope=="System"&&f.Key=="FileName"&&f.Value is TextValue text&&text.Value==Path.GetFileName(input));
            Assert.Contains(fields,f=>f.Scope=="System"&&f.Key=="Directory"&&f.Value is TextValue text&&text.Value==Path.GetDirectoryName(input));
            var identity=new ImageAdjustments(1,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,1,PhotoEffect.None,0);
            var doc=new EditorDocument(1,0,[source],[],new ImageRecipe(source.Id,new(0,false,false,null,null,false),identity,[],null,[],[new(comment.Scope,comment.Key,MetadataEditAction.Set,new TextValue("After ✓"))]),new("png",root,"metadata",new QualityGoal(85),MetadataPolicy.PreserveSupported,new(255,255,255,255)));
            var engine=(IConversionEngine)Activator.CreateInstance(engineType,new OutputWriter(),service,new SourceSnapshotService())!;
            var result=await engine.ExecuteAsync(new(engine.Operations.Single(),[input],new(),root,doc),null,default);
            var saved=(await new SourceSnapshotService().CaptureAsync([result.Outputs.Single()],default))[0].Snapshot!;
            Assert.Contains(await service.ReadAsync(saved,default),f=>f.Key=="Artist"&&f.Value is TextValue{Value:"After ✓"});
            using var before=new MagickImage(input);using var after=new MagickImage(saved.Path);
            Assert.Equal(before.GetPixels().ToByteArray(PixelMapping.RGBA),after.GetPixels().ToByteArray(PixelMapping.RGBA));
            Assert.Equal(source.Sha256,(await new SourceSnapshotService().CaptureAsync([input],default))[0].Snapshot!.Sha256);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task Metadata_argument_transport_keeps_newlines_inside_the_value_and_remove_all_is_verified()
    {
        string root=Path.Combine(Path.GetTempPath(),"citrus-metadata-arguments-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"image.png");using(var image=new MagickImage(MagickColors.Blue,7,5)){var exif=new ExifProfile();exif.SetValue(ExifTag.Artist,"Before");image.SetProfile(exif);image.Write(path);}
            var service=new Citrus.Conversion.Editing.Image.ExifToolMetadataService(FindTool());
            var field=Assert.Single(await service.ReadPathAsync(path,default),f=>f.Key=="Artist");
            string value="第一行 ✓ mail@example.com $quoted\n-config=not-a-command\nSecond line";
            await service.ApplyAsync(path,[new(field.Scope,field.Key,MetadataEditAction.Set,new TextValue(value))],default);
            Assert.Contains(await service.ReadPathAsync(path,default),f=>f.Key=="Artist"&&f.Value is TextValue text&&text.Value==value);
            await service.ApplyAsync(path,[],default,removeAll:true);
            Assert.DoesNotContain(await service.ReadPathAsync(path,default),f=>f.Removable);
            using var saved=new MagickImage(path);Assert.Equal(7u,saved.Width);Assert.Equal(5u,saved.Height);
        }
        finally{Directory.Delete(root,true);}
    }
    private static string FindTool()
    {
        string? directory=AppContext.BaseDirectory;
        while(directory is not null){string path=Path.Combine(directory,"packaging","image","win-x64","exiftool","exiftool.exe");if(File.Exists(path))return path;directory=Path.GetDirectoryName(directory);}
        throw new FileNotFoundException("Bundled ExifTool is required for metadata acceptance.");
    }
}
