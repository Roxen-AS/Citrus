using System.IO;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Conversion;
using Citrus.Windows.Editors;
using Citrus.Windows.Editors.Image;
using ImageMagick;

namespace Citrus.Windows.Tests;

public sealed class EditorHostReviewRegressionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cancellation_callback_newer_preview_survives_outer_request(bool update)
    {
        var renderer = new ReentrantRenderer(); var session = new EditorSession(renderer); session.Update(EditorSessionTests.Document(1));
        Task<PreviewFrame?>? nested = null;
        renderer.OnCancel = () => { if (update) session.Update(EditorSessionTests.Document(2)); nested = session.PreviewAsync(Request(3),default); };
        var first = session.PreviewAsync(Request(1),default); var second = session.PreviewAsync(Request(2),default);
        renderer.CompleteAll();
        Assert.Null(await first); Assert.NotNull(nested); Assert.NotNull(await nested); Assert.Null(await second);
    }
    [Fact]
    public async Task Completing_old_preview_inside_cancellation_callback_keeps_new_work_alive()
    {
        var renderer = new ReentrantRenderer(inlineCompletion: true); var session = new EditorSession(renderer); session.Update(EditorSessionTests.Document(1));
        Task<PreviewFrame?>? nested = null;
        renderer.OnCancel = () => { renderer.Complete(1); nested = session.PreviewAsync(Request(3),default); };
        var first = session.PreviewAsync(Request(1),default); var second = session.PreviewAsync(Request(2),default);
        renderer.CompleteAll(); Assert.Null(await first); Assert.NotNull(await nested!); Assert.Null(await second); session.Close();
    }
    [Fact]
    public async Task Cancellation_callback_close_rejects_all_results_and_future_requests()
    {
        var renderer = new ReentrantRenderer(); var session = new EditorSession(renderer); session.Update(EditorSessionTests.Document(1));
        renderer.OnCancel = session.Close;
        var first = session.PreviewAsync(Request(1),default); var second = session.PreviewAsync(Request(2),default);
        renderer.CompleteAll(); Assert.Null(await first); Assert.Null(await second); Assert.Null(await session.PreviewAsync(Request(3),default));
    }
    [Theory]
    [InlineData("image.rotate","Rotate")] [InlineData("image.resize","Resize")] [InlineData("image.compress","Compress")]
    public async Task Explicit_reset_clears_entry_preset_to_identity_and_is_undoable(string id,string tool)
    {
        using var fixture = new Ramp(); using var services = new ImageEditorServices(new());
        var request = new EditorOpenRequest(new(id,tool,OperationMode.Tools,null,false,true),[fixture.Input],tool,new() { Width = 1200, Quality = 42 },null,SubmissionOrigin.Wheel);
        var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default);
        var row = model.Selected!; var initial = row.Draft!; var image = (ImageRecipe)initial.Recipe;
        if (tool == "Rotate") Assert.Equal(1,image.Geometry.QuarterTurns);
        if (tool == "Resize") Assert.Equal(new SizeI(1200,840),image.Geometry.OutputSize);
        model.Change(initial with { Recipe = image with { Geometry = image.Geometry with { FlipX = true }, Adjustments = image.Adjustments with { Brightness = .2, Effect = PhotoEffect.Sepia } } });
        var before = row.Draft!; model.Reset(); var reset = row.Draft!; var neutral = (ImageRecipe)reset.Recipe;
        Assert.Equal(new ImageGeometry(0,false,false,null,null,false),neutral.Geometry);
        Assert.Equal(0,neutral.Adjustments.Brightness); Assert.Equal(PhotoEffect.None,neutral.Adjustments.Effect);
        Assert.Equal(new QualityGoal(85),reset.Export.Compression); Assert.Null(row.ResizeIntent);
        Assert.Same(row,model.Selected); Assert.True(row.Included); Assert.Equal(initial.Sources,reset.Sources);
        Assert.Equal(before.Export with { Compression = new QualityGoal(85) },reset.Export);
        model.Undo(); Assert.Equal(before.Recipe,row.Draft!.Recipe); Assert.Equal(before.Export,row.Draft.Export);
        model.Redo(); Assert.Equal(reset.Recipe,row.Draft!.Recipe);
        Assert.Same(request.Operation,model.Accept().Request.Operation);
    }
    [Theory]
    [InlineData(0,1200,840,false)] [InlineData(1,840,1200,false)]
    [InlineData(2,913,777,true)] [InlineData(3,913,777,true)]
    [InlineData(0,300,210,false)]
    public async Task Exact_detail_matches_native_export_center_pixels_after_rotation_crop_and_resize(int turns,int width,int height,bool crop)
    {
        using var fixture = new Ramp(); using var services = new ImageEditorServices(new());
        var request = new EditorOpenRequest(new("image.editor","Adjust",OperationMode.Tools,null,false,true),[fixture.Input],"Adjust",new(),null,SubmissionOrigin.Home);
        var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default);
        var initial = model.Selected!.Draft!; var recipe = ((ImageRecipe)initial.Recipe) with
        {
            Geometry = new(turns,true,true,crop ? new RectD(23,17,350,400) : null,new(width,height),true)
        };
        var document = initial with { Recipe = recipe }; using var renderer = services.CreateSessionRenderer();
        var frame = await renderer.RenderPreviewAsync(document,ImageDetailPreview.Create(recipe,new(600,420),1,new(512,512)),default);
        int tileWidth = Math.Min(width,512), tileHeight = Math.Min(height,512);
        Assert.Equal(new SizeI(tileWidth,tileHeight),frame.Size); Assert.Empty(frame.Warnings);
        string output = Path.Combine(fixture.Root,"export"); var staged = await renderer.RenderExportAsync(document,output,null,default);
        using var stream = File.OpenRead(Path.Combine(output,"payload",staged.Files.Single().RelativePath));
        using var image = new MagickImage(stream,new MagickReadSettings { Format = MagickFormat.Png });
        Assert.Equal((uint)width,image.Width); Assert.Equal((uint)height,image.Height);
        image.Crop(new MagickGeometry((width-tileWidth)/2,(height-tileHeight)/2,(uint)tileWidth,(uint)tileHeight)); image.ResetPage();
        Assert.Equal(image.GetPixels().ToByteArray(PixelMapping.BGRA)!,frame.Bgra32.ToArray());
    }
    private static PreviewRequest Request(long generation) => new(generation,new(512,512),null,null,null);
    private sealed class ReentrantRenderer(bool inlineCompletion = false) : IEditorRenderer
    {
        private readonly Dictionary<long,(EditorDocument Document,TaskCompletionSource<PreviewFrame> Completion)> pending = [];
        public Action? OnCancel { get; set; }
        public Task<PreviewFrame> RenderPreviewAsync(EditorDocument document,PreviewRequest request,CancellationToken ct)
        {
            var completion = new TaskCompletionSource<PreviewFrame>(inlineCompletion ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(request.Generation,(document,completion));
            if (request.Generation == 1) ct.Register(() => OnCancel!());
            return completion.Task;
        }
        public void Complete(long generation) { var item = pending[generation]; item.Completion.TrySetResult(new(item.Document.Revision,generation,new(1,1),4,[0,0,0,255],null,null,[])); }
        public void CompleteAll() { foreach (long generation in pending.Keys.ToArray()) Complete(generation); }
        public Task<StagedExport> RenderExportAsync(EditorDocument doc,string path,IProgress<ConversionProgress>? progress,CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Ramp : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"citrus-host-regression-"+Guid.NewGuid().ToString("N"));
        public string Input => Path.Combine(Root,"ramp.png");
        public Ramp()
        {
            Directory.CreateDirectory(Root); byte[] pixels = new byte[600*420*4];
            for (int y=0;y<420;y++) for (int x=0;x<600;x++)
            {
                int p = (y*600+x)*4; pixels[p] = (byte)(x%256); pixels[p+1] = (byte)(y%256); pixels[p+2] = (byte)((x*17+y*31)%256); pixels[p+3] = 255;
            }
            using var image = new MagickImage(pixels,new PixelReadSettings(600,420,StorageType.Char,PixelMapping.RGBA)); image.Write(Input,MagickFormat.Png);
        }
        public void Dispose() => Directory.Delete(Root,true);
    }
}
