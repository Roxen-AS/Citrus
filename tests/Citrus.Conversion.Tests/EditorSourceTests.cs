using System.Collections.Immutable;
using System.Security.Cryptography;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Conversion.Editing;

namespace Citrus.Conversion.Tests;

public sealed class EditorSourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),"citrus-editor-" + Guid.NewGuid().ToString("N"));
    public EditorSourceTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root,true);
    private string FilePath(string name) => Path.Combine(root,name);
    private async Task<SourceSnapshot> Capture(string name = "source.png")
    {
        string path = FilePath(name);
        await File.WriteAllBytesAsync(path,[1,2,3,4]);
        return (await new SourceSnapshotService().CaptureAsync([path],default))[0].Snapshot!;
    }
    private static EditorDocument Image(SourceSnapshot source) => new(1,0,[source],[],new ImageRecipe(source.Id,new(0,false,false,null,null,false),new(1,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,1,PhotoEffect.None,0),[],null,[],[]),new(Path.GetExtension(source.Path)[1..],null,"result",new QualityGoal(85),MetadataPolicy.PreserveSupported,new(255,255,255,255)));
    private static OperationRequest Request(SourceSnapshot source, bool directory = false)
    {
        var doc = Image(source);
        if (directory) doc = doc with { Recipe = new PdfRecipe([new(Guid.NewGuid(),source.Id,1,0)],PdfOutputMode.PageImages,300,PdfCompressionMode.PreserveStructure,85,[]), Export = doc.Export with { FormatId = "png" } };
        return new(new(directory ? "pdf.images" : "image.resize","Export",OperationMode.Tools,null,false,true),[source.Path],new(),null,doc);
    }
    private sealed class Validator(bool reject = false) : IEditorPayloadValidator
    {
        public Task ValidateAsync(OperationRequest request,StagedExport staged,string privateDirectory,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (reject) throw new InvalidDataException("Independent decoder rejected output");
            return Task.CompletedTask;
        }
    }
    private static EditorOutputPublisher Publisher(bool reject = false) => new(new(),new(),new Validator(reject));
    private async Task<(string directory,StagedExport staged)> Payload(bool directory = false,string name = "frame.png")
    {
        string stage = FilePath(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stage,"payload"));
        await File.WriteAllBytesAsync(Path.Combine(stage,"payload",name),[5,6,7]);
        return (stage,new(directory ? ExportShape.Directory : ExportShape.File,[new(name,3)],new(4,3,null,true,1),[]));
    }

    [Fact]
    public async Task Same_length_same_mtime_replacement_fails_sha256()
    {
        var source = await Capture();
        await File.WriteAllBytesAsync(source.Path,[8,7,6,5]);
        File.SetLastWriteTimeUtc(source.Path,new DateTime(source.LastWriteUtcTicks,DateTimeKind.Utc));
        string stage = FilePath("private");
        await Assert.ThrowsAsync<IOException>(() => new SourceSnapshotService().StageVerifiedAsync([source],stage,default));
        Assert.False(Directory.Exists(stage));
        Assert.Equal(new byte[] {8,7,6,5},await File.ReadAllBytesAsync(source.Path));
    }

    [Fact]
    public async Task Verified_copy_survives_later_original_change()
    {
        var source = await Capture();
        string before = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source.Path)));
        var copy = await new SourceSnapshotService().StageVerifiedAsync([source],FilePath("private"),default);
        Assert.Equal(before,Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source.Path))));
        await File.WriteAllBytesAsync(source.Path,[9,9,9,9]);
        Assert.Equal(new byte[] {1,2,3,4},await File.ReadAllBytesAsync(copy[source.Id]));
        Assert.StartsWith(FilePath("private"),copy[source.Id]);
    }

    [Fact]
    public async Task Cancel_during_copy_cleans_private_directory()
    {
        var source = await Capture("large.png");
        // A sparse 128 MiB file ensures cancellation can interrupt asynchronous copy after ownership begins.
        using (var file = File.OpenWrite(source.Path)) file.SetLength(128 * 1024 * 1024);
        source = (await new SourceSnapshotService().CaptureAsync([source.Path],default))[0].Snapshot!;
        using var cancellation = new CancellationTokenSource();
        string stage = FilePath("private");
        var copying = new SourceSnapshotService().StageVerifiedAsync([source],stage,cancellation.Token);
        while (!Directory.Exists(Path.Combine(stage,"sources")) && !copying.IsCompleted) await Task.Yield();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copying);
        Assert.False(Directory.Exists(stage));
    }

    [Fact]
    public async Task Capture_hash_failure_has_error_without_snapshot_or_recipe()
    {
        var results = await new SourceSnapshotService().CaptureAsync([FilePath("absent.png"),FilePath("also-absent.png")],default);
        Assert.Equal(2,results.Length);
        Assert.All(results,r => { Assert.Null(r.Snapshot); Assert.False(string.IsNullOrWhiteSpace(r.Error)); });
        Assert.Equal(FilePath("absent.png"),results[0].Path);
    }

    [Theory]
    [InlineData("../outside.png",3)]
    [InlineData("/outside.png",3)]
    [InlineData("frame.png",100)]
    [InlineData("frame.png:stream",3)]
    public async Task Publisher_rejects_escape_paths_and_size_lies(string name,long bytes)
    {
        var source = await Capture(); var payload = await Payload();
        await Assert.ThrowsAnyAsync<Exception>(() => Publisher().PublishAsync(Request(source),payload.staged with { Files = [new(name,bytes)] },payload.directory,default));
        Assert.False(File.Exists(FilePath("result.png")));
        Assert.Empty(Directory.GetFileSystemEntries(root,".citrus-*"));
    }

    [Fact]
    public async Task Grouped_publish_collision_preserves_existing_folder()
    {
        var source = await Capture("source.pdf"); var payload = await Payload(true);
        Directory.CreateDirectory(FilePath("result")); await File.WriteAllTextAsync(Path.Combine(FilePath("result"),"keep"),"original");
        var result = await Publisher().PublishAsync(Request(source,true),payload.staged,payload.directory,default);
        Assert.Equal(FilePath("result (1)"),Assert.Single(result.Outputs));
        Assert.Equal("original",await File.ReadAllTextAsync(Path.Combine(FilePath("result"),"keep")));
        Assert.True(File.Exists(Path.Combine(result.Outputs[0],"frame.png")));
    }

    [Fact]
    public async Task One_member_directory_stays_directory()
    {
        var source = await Capture("source.pdf"); var payload = await Payload(true);
        var result = await Publisher().PublishAsync(Request(source,true),payload.staged,payload.directory,default);
        Assert.True(Directory.Exists(Assert.Single(result.Outputs)));
        Assert.Equal(3,result.Compression!.ActualBytes);
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("extension")]
    [InlineData("stem")]
    [InlineData("unlisted")]
    [InlineData("directory")]
    [InlineData("collision")]
    public async Task Wrong_shape_extension_reserved_stem_or_unlisted_payload_rejected(string reason)
    {
        var source = await Capture(); var payload = await Payload(); var request = Request(source); var staged = payload.staged;
        if (reason == "shape") staged = staged with { Shape = ExportShape.Directory };
        if (reason == "extension") { File.Move(Path.Combine(payload.directory,"payload","frame.png"),Path.Combine(payload.directory,"payload","frame.jpg")); staged = staged with { Files = [new("frame.jpg",3)] }; }
        if (reason == "stem") request = request with { Editor = request.Editor! with { Export = request.Editor.Export with { OutputStem = "CON.txt" } } };
        if (reason == "unlisted") await File.WriteAllTextAsync(Path.Combine(payload.directory,"payload","secret.txt"),"secret");
        if (reason == "directory") Directory.CreateDirectory(Path.Combine(payload.directory,"payload","nested"));
        if (reason == "symlink") { File.Delete(Path.Combine(payload.directory,"payload","frame.png")); File.CreateSymbolicLink(Path.Combine(payload.directory,"payload","frame.png"),source.Path); }
        if (reason == "collision") staged = staged with { Files = [new("frame.png",3),new("FRAME.PNG",3)] };
        await Assert.ThrowsAnyAsync<Exception>(() => Publisher().PublishAsync(request,staged,payload.directory,default));
        Assert.False(File.Exists(FilePath("result.png")));
    }

    [LinuxEditorLinkFact]
    public Task Payload_symlink_is_rejected() => Wrong_shape_extension_reserved_stem_or_unlisted_payload_rejected("symlink");

    [Theory]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("tif")]
    [InlineData("tiff")]
    public async Task Source_format_jpg_jpeg_tif_tiff_aliases_retain_original_suffix(string suffix)
    {
        var source = await Capture("source." + suffix); var payload = await Payload(name:"frame." + suffix);
        var target = new ExportTargetResolver().Resolve(Request(source));
        Assert.Equal("." + suffix,target.Extension);
        var output = await Publisher().PublishAsync(Request(source),payload.staged,payload.directory,default);
        Assert.Equal(FilePath("result." + suffix),Assert.Single(output.Outputs));
    }

    [Fact]
    public async Task Independent_payload_validation_failure_leaves_destination_untouched()
    {
        var source = await Capture(); var payload = await Payload();
        await Assert.ThrowsAsync<InvalidDataException>(() => Publisher(true).PublishAsync(Request(source),payload.staged,payload.directory,default));
        Assert.False(File.Exists(FilePath("result.png")));
    }

    [Fact]
    public async Task Scalar_engine_rejects_editor_payload()
    {
        var source = await Capture(); var request = Request(source);
        var image = new ImageEngine();
        Assert.True(image.CanExecute(request with { Editor = null }));
        Assert.False(image.CanExecute(request));
        var pdfSource = await Capture("document.pdf");
        var pdfRequest = Request(pdfSource,true);
        var pdf = new PdfEngine();
        Assert.True(pdf.CanExecute(pdfRequest with { Editor = null }));
        Assert.False(pdf.CanExecute(pdfRequest));
        var media = await MediaEngine.CreateAsync(new MediaProcess(Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG") ?? "/usr/bin/ffmpeg",Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE") ?? "/usr/bin/ffprobe"));
        var audio = await Capture("tone.wav");
        var mediaRequest = request with { Operation = media.Operations.Single(o => o.Id == "audio.trim"), Inputs = [audio.Path] };
        Assert.True(media.CanExecute(mediaRequest with { Editor = null }));
        Assert.False(media.CanExecute(mediaRequest));
    }
    [Fact]
    public async Task Compression_original_bytes_counts_roots_not_auxiliary_artifacts()
    {
        var source = await Capture(); var extra = await Capture("background.png"); var payload = await Payload();
        var request = Request(source);
        var recipe = (ImageRecipe)request.Editor!.Recipe;
        request = request with { Editor = request.Editor with { Sources = [source,extra], Recipe = recipe with { Canvas = new(new(10,10),new ImageBackground(extra.Id,ImageFit.Cover,new(.5,.5),1,0),new(0,0,10,10),ImageFit.Contain,0,null) } } };
        var result = await Publisher().PublishAsync(request,payload.staged,payload.directory,default);
        Assert.Equal(4,result.Compression!.OriginalBytes);
    }

    [Fact]
    public async Task Router_dispatches_only_injected_family_and_never_advertises_routes()
    {
        var source = await Capture(); var doc = Image(source);
        var renderer = new Renderer();
        var router = new EditorRenderRouter(new Dictionary<Type,IEditorRenderer> { [typeof(ImageRecipe)] = renderer });
        var frame = await router.RenderPreviewAsync(doc,new(7,new(1,1),null,null,null),default);
        Assert.Equal(7,frame.Generation);
        Assert.Equal(doc.Revision,frame.Revision);
        Assert.False(new EditorRouteRegistry().TryGet("image.resize",out _));
        await Assert.ThrowsAsync<NotSupportedException>(() => new EditorRenderRouter(new Dictionary<Type,IEditorRenderer>()).RenderExportAsync(doc,FilePath("private"),null,default));
    }
    private sealed class Renderer : IEditorRenderer
    {
        public Task<PreviewFrame> RenderPreviewAsync(EditorDocument doc,PreviewRequest request,CancellationToken ct) => Task.FromResult(new PreviewFrame(doc.Revision,request.Generation,new(1,1),4,[1,2,3,255],null,null,[]));
        public Task<StagedExport> RenderExportAsync(EditorDocument doc,string directory,IProgress<ConversionProgress>? progress,CancellationToken ct) => throw new NotSupportedException();
    }

}

// Windows symlink creation requires host privileges; the native reparse acceptance gate remains explicit.
public sealed class LinuxEditorLinkFactAttribute : FactAttribute
{
    public LinuxEditorLinkFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Linux symlink surrogate; native Windows reparse checks require a privileged Windows fixture.";
    }
}
