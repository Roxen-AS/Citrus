using Citrus.Conversion.Editing.Media;
using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Tests;

public sealed class MediaNestedMetadataTests : IAsyncLifetime
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"citrus-nested-tags-"+Guid.NewGuid().ToString("N"));
    private MediaProcess process=null!;
    private MediaEditorEngine engine=null!;
    private string video="";
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        process=new(Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG")??"/usr/bin/ffmpeg",Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE")??"/usr/bin/ffprobe");
        engine=new(process,await process.DiscoverCapabilitiesAsync(default),new());
        string raw=Path.Combine(root,"frames.bgra");
        using(var file=File.Create(raw))for(int frame=0;frame<12;frame++)for(int i=0;i<32*24;i++)file.Write([32,64,192,255]);
        video=Path.Combine(root,"source.mp4");
        await process.RunAsync(["-f","rawvideo","-pixel_format","bgra","-video_size","32x24","-framerate","12","-i",raw,"-c:v","mpeg4","-an",video],null,default);
    }
    public Task DisposeAsync(){Directory.Delete(root,true);return Task.CompletedTask;}
    private async Task<(string Path,EditorDocument Doc)> Tagged(string extension)
    {
        string chapters=Path.Combine(root,"chapters.txt");
        await File.WriteAllTextAsync(chapters,";FFMETADATA1\ntitle=Private global\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1000\ntitle=Private chapter\ncomment=Private chapter comment\n");
        string output=Path.Combine(root,"tagged."+extension);
        await process.RunAsync([..MediaProcess.InputArguments(video),"-f","ffmetadata","-i",chapters,"-map","0","-map_metadata","1","-map_chapters","1","-metadata:s:v:0","title=Private stream","-metadata:s:v:0","handler_name=Private handler","-metadata:s:v:0","language=eng","-c","copy",output],null,default);
        var source=(await engine.Sources.CaptureAsync([output],default)).Single().Snapshot!;
        var doc=await engine.CreateDocumentAsync(engine.Operations.Single(o=>o.Id=="video.metadata"),source,new(),root,default);
        return (output,doc);
    }
    [Theory]
    [InlineData("mkv","title")]
    [InlineData("mp4","handler_name")]
    public async Task Metadata_view_exposes_nested_writable_fields(string extension,string streamKey)
    {
        var (_,doc)=await Tagged(extension);var fields=await engine.ReadMetadataAsync(doc.Sources[0],default);
        Assert.Contains(fields,f=>f.Scope=="stream:0"&&f.Key.Equals(streamKey,StringComparison.OrdinalIgnoreCase)&&f.Writable&&f.Removable);
        Assert.Contains(fields,f=>f.Scope=="chapter:0"&&f.Key.Equals("title",StringComparison.OrdinalIgnoreCase)&&f.Removable);
    }
    [Theory]
    [InlineData("mkv")]
    [InlineData("mp4")]
    public async Task Remove_all_clears_private_global_stream_and_chapter_tags(string extension)
    {
        var (path,doc)=await Tagged(extension);doc=doc with {Export=doc.Export with {Metadata=MetadataPolicy.StripAll}};
        string output=Assert.Single((await engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id=="video.metadata"),[path],new(),root,doc),null,default)).Outputs);
        using var json=await process.ProbeJsonAsync(["-show_format","-show_streams","-show_chapters","-of","json",..MediaProcess.InputArguments(output)],1024*1024,default);
        Assert.DoesNotContain("Private",json.RootElement.GetRawText(),StringComparison.OrdinalIgnoreCase);
        var chapter=Assert.Single(json.RootElement.GetProperty("chapters").EnumerateArray());
        // MP4's chapter atom has a required title slot even when its source tag was removed.
        if(extension=="mp4")Assert.Equal("",chapter.GetProperty("tags").GetProperty("title").GetString());
    }
    [Theory]
    [InlineData("mkv","title")]
    [InlineData("mp4","handler_name")]
    public async Task Scoped_field_edits_round_trip_without_reencoding(string extension,string key)
    {
        var (path,doc)=await Tagged(extension);
        doc=doc with {Recipe=((VideoRecipe)doc.Recipe) with {MetadataEdits=[new("stream:0",key,MetadataEditAction.Set,new TextValue("Changed stream")),new("chapter:0","title",MetadataEditAction.Set,new TextValue("Changed chapter"))]}};
        string output=Assert.Single((await engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id=="video.metadata"),[path],new(),root,doc),null,default)).Outputs);
        var source=(await engine.Sources.CaptureAsync([output],default)).Single().Snapshot!;
        var fields=await engine.ReadMetadataAsync(source,default);
        Assert.Contains(fields,f=>f.Scope=="stream:0"&&f.Key.Equals(key,StringComparison.OrdinalIgnoreCase)&&f.Value is TextValue {Value:"Changed stream"});
        Assert.Contains(fields,f=>f.Scope=="chapter:0"&&f.Key.Equals("title",StringComparison.OrdinalIgnoreCase)&&f.Value is TextValue {Value:"Changed chapter"});
    }

    [Theory]
    [InlineData("chapter:0","title","",true)]
    [InlineData("chapter:0","title","Private chapter",false)]
    [InlineData("chapter:0","comment","",false)]
    [InlineData("stream:0","title","",false)]
    [InlineData("format","title","",false)]
    public void Regenerated_chapter_title_exception_cannot_accept_a_private_value(string scope,string key,string value,bool allowed)
        =>Assert.Equal(allowed,MediaMetadataScopes.RegeneratedDefault(new(scope,key,value)));
}
