using System.IO;
using System.Collections.Immutable;
using Citrus.Conversion;
using Citrus.Conversion.Editing;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Windows.Editors;
using Citrus.Windows.Editors.Image;
using ImageMagick;

namespace Citrus.Windows.Tests;

public sealed class ImageEditorModelTests
{
    [Fact]
    public async Task Uncaptured_path_requires_explicit_exclusion_and_selection_never_changes_inclusion()
    {
        using var files = new Images(); using var services = new ImageEditorServices(new());
        var request = files.Request("image.crop", "Crop", [files.A, files.Missing]);
        var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default);
        Assert.Null(model.Sources[1].Source); Assert.Null(model.Sources[1].Draft); Assert.NotNull(model.Sources[1].Error);
        model.Selected = model.Sources[0]; Assert.True(model.Sources[1].Included);
        Assert.Throws<ArgumentException>(() => model.Accept()); model.Sources[1].Included = false;
        var batch = model.Accept(); Assert.Same(request, batch.Request); Assert.Equal(new[] { files.Missing },batch.ExcludedInitialPaths.ToArray());
        Assert.Single(batch.InitialSources); Assert.Single(batch.Items); Assert.Throws<InvalidOperationException>(() => model.Accept());
    }
    [Fact]
    public async Task Relative_batch_maps_each_root_and_pixel_failure_changes_no_history()
    {
        using var files = new Images(); using var services = new ImageEditorServices(new());
        var request = files.Request("image.crop","Crop",[files.A,files.B]);
        var model = new EditorHostViewModel(request, services); await model.CaptureAsync(request.InitialPaths,false,default);
        model.Selected = model.Sources[0];
        var doc = model.Selected.Draft!; var recipe = (ImageRecipe)doc.Recipe;
        model.Change(doc with { Recipe = recipe with { Geometry = recipe.Geometry with { Crop = new(40,20,60,30) } } });
        var before = model.Sources[1].Draft;
        Assert.Throws<ArgumentException>(() => model.ApplyToAll(ImageBatchGeometry.Pixels,null)); Assert.Same(before,model.Sources[1].Draft);
        model.ApplyToAll(ImageBatchGeometry.Relative,null);
        Assert.Equal(new RectD(20,10,30,15), ((ImageRecipe)model.Sources[1].Draft!.Recipe).Geometry.Crop);
        var batch = model.Accept(); Assert.Equal(2,batch.Items.Length);
        Assert.Equal("png",batch.Items[0].Document.Export.FormatId); Assert.Equal("jpg",batch.Items[1].Document.Export.FormatId);
        Assert.NotEqual(((ImageRecipe)batch.Items[0].Document.Recipe).SourceId,((ImageRecipe)batch.Items[1].Document.Recipe).SourceId);
    }
    [Fact]
    public async Task Gesture_is_one_history_entry_and_retry_keeps_frozen_recipe_after_host_changes()
    {
        using var files = new Images(); using var services = new ImageEditorServices(new());
        var request = files.Request("image.resize","Resize",[files.A]);
        var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default);
        var initial = model.Selected!.Draft!;
        for (int i=1;i<=20;i++) { var d = model.Selected.Draft!; model.Change(d with { Recipe = ((ImageRecipe)d.Recipe) with { Adjustments = ((ImageRecipe)d.Recipe).Adjustments with { Brightness = i/100d } } }, true); }
        model.CommitGesture(); model.Undo(); Assert.Equal(initial.Recipe,model.Selected.Draft!.Recipe);
        model.Redo(); Assert.Equal(.2,((ImageRecipe)model.Selected.Draft!.Recipe).Adjustments.Brightness);
        var batch = model.Accept(); var queue = new JobQueue([services.Engine]); var submission = new EditorSubmissionService(queue,services.Routes,services.Inspector);
        var job = Assert.Single(await submission.SubmitAsync(batch,default)); await job.Completion; Assert.Equal(ConversionJobState.Succeeded,job.State);
        model.Reset(); Assert.Equal(.2,((ImageRecipe)job.Request.Editor!.Recipe).Adjustments.Brightness);
        var retry = await submission.RetryAsync(job.Request,null,default); await retry.Completion;
        Assert.Equal(job.Request.Editor.Recipe,retry.Request.Editor!.Recipe); Assert.Equal(request.Operation,retry.Request.Operation); Assert.Equal(request.InitialOptions,retry.Request.Options);
        Assert.Equal(ConversionJobState.Succeeded,retry.State); Assert.NotEqual(job.Outputs[0],retry.Outputs[0]);
    }
    [Fact]
    public async Task Explicit_import_and_width_only_batch_preserve_initial_payload_and_per_root_ratio()
    {
        using var files = new Images(); using var services = new ImageEditorServices(new());
        var request = files.Request("image.resize","Resize",[files.A]);
        var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default); await model.CaptureAsync([files.B],true,default);
        var resize = new ImageResizeIntent(20,null,true,true); model.ApplyToAll(ImageBatchGeometry.Relative,resize);
        var batch = model.Accept(); Assert.Equal(new[] { files.A },batch.Request.InitialPaths.ToArray()); Assert.Single(batch.Imports); Assert.Equal(EditorImportRole.Root,batch.Imports[0].Role);
        Assert.Equal(new SizeI(20,10),((ImageRecipe)batch.Items[1].Document.Recipe).Geometry.OutputSize);
    }
    [Fact]
    public void Crop_drag_clamps_and_ratio_resize_stays_within_source()
    {
        var moved = CropGestureGeometry.Drag(new(10,20,40,30),new(100,80),-1,100,-100,null);
        Assert.Equal(new RectD(60,0,40,30),moved);
        foreach (int corner in Enumerable.Range(0,4))
        {
            var crop = CropGestureGeometry.Drag(new(20,20,40,30),new(100,80),corner,200,-200,1);
            Assert.Equal(crop.Width,crop.Height); Assert.InRange(crop.X,0,100-crop.Width); Assert.InRange(crop.Y,0,80-crop.Height);
        }
    }
    [Fact]
    public void Every_exposed_adjustment_writes_only_its_supported_recipe_field()
    {
        var a = new ImageAdjustments(1,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,1,PhotoEffect.None,1);
        Assert.Equal(21,ImageEditorViewModel.Fields.Count);
        foreach (var field in ImageEditorViewModel.Fields)
        {
            var changed = field.Write(a,field.Maximum);
            Assert.Equal(field.Maximum,field.Read(changed)); Assert.Equal(a.AlgorithmVersion,changed.AlgorithmVersion);
            foreach (var other in ImageEditorViewModel.Fields.Where(f => f != field)) Assert.Equal(other.Read(a),other.Read(changed));
        }
    }
    [Fact]
    public async Task Capability_verification_does_real_preview_admission_and_validated_export()
    {
        using var services = new ImageEditorServices(new()); await services.VerifyAsync(default);
        Assert.True(services.Routes.TryGet("image.crop",out var route)); Assert.Equal(EditorSubmissionShape.PerRoot,route.Shape);
        Assert.True(services.Routes.TryGet("image.annotate",out var annotate)); Assert.Equal("Annotate",annotate.EntryTool); Assert.Equal(EditorSubmissionShape.PerRoot,annotate.Shape);
        Assert.True(services.Routes.TryGet("image.redact",out var redact)); Assert.Equal("Manual masks",redact.EntryTool); Assert.Equal(EditorSubmissionShape.PerRoot,redact.Shape);
        Assert.False(services.Routes.TryGet("images.pdf",out _)); Assert.False(services.Routes.TryGet("image.convert.png",out _));
    }
    [Fact]
    public async Task Captured_but_unreadable_source_blocks_export_until_explicit_exclusion()
    {
        using var files = new Images(); using var services = new ImageEditorServices(new());
        File.WriteAllText(files.Missing,"not image pixels");
        var request = files.Request("image.editor","Adjust",[files.A,files.Missing]);
        var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default);
        Assert.NotNull(model.Sources[1].Source); Assert.Null(model.Sources[1].Draft); Assert.NotNull(model.Sources[1].Error);
        Assert.Throws<ArgumentException>(() => model.Accept()); model.Sources[1].Included = false;
        var batch = model.Accept(); Assert.Equal(2,batch.InitialSources.Length); Assert.Single(batch.Items);
    }
    [Fact]
    public async Task Source_mutation_blocks_admission_and_reload_captures_new_identity()
    {
        using var files = new Images(); using var services = new ImageEditorServices(new());
        var request = files.Request("image.editor","Adjust",[files.A]);
        var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default);
        var oldSource = model.Selected!.Source!; var oldDocument = model.Selected.Draft!;
        using (var changed = new MagickImage(MagickColors.Green,80,40)) changed.Write(files.A,MagickFormat.Png);
        var issues = await services.Inspector.ValidateAsync(new(request.Operation,[files.A],new(),null,oldDocument),default);
        Assert.NotEmpty(issues);
        await model.ReloadAsync(model.Selected,default); Assert.NotEqual(oldSource.Id,model.Selected.Source!.Id);
        Assert.Equal(new SizeI(80,40),model.Selected.Size); Assert.Null(model.Selected.Error);
        var batch = model.Accept(); Assert.Equal(model.Selected.Source,batch.InitialSources.Single());
    }
    [Fact]
    public async Task Legacy_presets_keep_identity_flags_options_and_source_format()
    {
        using var files = new Images(); using var services = new ImageEditorServices(new());
        foreach (var (id,tool) in new[] { ("image.resize","Resize"), ("image.rotate","Rotate"), ("image.compress","Compress") })
        {
            var request = files.Request(id,tool,[files.A]) with { InitialOptions = new() { Width = 200, RotationDegrees = 270, Quality = 63 } };
            var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default);
            var batch = model.Accept(); Assert.Same(request.Operation,batch.Request.Operation); Assert.Same(request.InitialOptions,batch.Request.InitialOptions);
            var image = (ImageRecipe)batch.Items.Single().Document.Recipe;
            if (tool == "Resize") Assert.Equal(new SizeI(200,100),image.Geometry.OutputSize);
            if (tool == "Rotate") Assert.Equal(3,image.Geometry.QuarterTurns);
            Assert.Equal(new QualityGoal(63),batch.Items.Single().Document.Export.Compression);
            Assert.False(batch.Request.Operation.IsGroup); Assert.True(batch.Request.Operation.NeedsOptions);
        }
    }
    [Theory]
    [InlineData(85)] [InlineData(1)] [InlineData(100)]
    public async Task Compression_opens_percentage_by_default_and_preserves_nondefault_quality(int quality)
    {
        using var files = new Images(); using var services = new ImageEditorServices(new());
        var request = files.Request("image.compress","Compress",[files.A]) with { InitialOptions = new() { Quality = quality } };
        var model = new EditorHostViewModel(request,services); await model.CaptureAsync(request.InitialPaths,false,default);
        var compression = model.Accept().Items.Single().Document.Export.Compression;
        if (quality == 85) Assert.Equal(new ReductionGoal(50),compression);
        else Assert.Equal(new QualityGoal(quality),compression);
    }
    private sealed class Images : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"citrus-host-test-"+Guid.NewGuid().ToString("N"));
        public string A => Path.Combine(Root,"wide.png"); public string B => Path.Combine(Root,"small.jpg"); public string Missing => Path.Combine(Root,"missing.png");
        public Images() { Directory.CreateDirectory(Root); using var a = new MagickImage(MagickColors.Orange,100,50); a.Write(A,MagickFormat.Png); using var b = new MagickImage(MagickColors.Blue,50,25); b.Write(B,MagickFormat.Jpeg); }
        public EditorOpenRequest Request(string id,string tool,ImmutableArray<string> paths) => new(new(id,tool,OperationMode.Tools,null,false,true),paths,tool,new(),null,SubmissionOrigin.Wheel);
        public void Dispose() => Directory.Delete(Root,true);
    }
}
