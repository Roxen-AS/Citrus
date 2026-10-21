using System.Collections.Immutable;
using Citrus.Core.Editing;

namespace Citrus.Core.Tests;

public sealed class EditorSubmissionTests
{
    private static readonly OperationDescriptor Resize = new("image.resize","Resize",OperationMode.Tools,null,false,true);
    private sealed class Inspector(Func<OperationRequest,CancellationToken,Task<ImmutableArray<ValidationIssue>>>? inspect = null) : IEditorAdmissionInspector
    {
        public Task<ImmutableArray<ValidationIssue>> ValidateAsync(OperationRequest request,CancellationToken ct) => inspect?.Invoke(request,ct) ?? Task.FromResult(ImmutableArray<ValidationIssue>.Empty);
    }
    private sealed class Engine(OperationDescriptor operation) : IConversionEngine
    {
        public int Executed;
        public IReadOnlyList<OperationDescriptor> Operations => [operation];
        public bool CanExecute(OperationRequest request) => request.Editor is not null;
        public Task<ConversionResult> ExecuteAsync(OperationRequest request,IProgress<ConversionProgress>? progress,CancellationToken ct)
        {
            Interlocked.Increment(ref Executed);
            if (Path.GetFileName(request.Inputs[0]).Contains("fail", StringComparison.Ordinal)) throw new IOException("Independent execution failure");
            return Task.FromResult(new ConversionResult([request.Editor!.Export.OutputStem],new(100,20,null,true,1)));
        }
    }
    private static EditorSubmissionBatch Batch(params string[] paths)
    {
        var sources = paths.Select(EditorTestData.Source).ToImmutableArray();
        return new(new(Resize,[..paths],"Resize",new(),null,SubmissionOrigin.Wheel),sources,[],[],[..sources.Select(s => s.Id)],
            [..sources.Select(s => new EditorSubmissionItem(Guid.NewGuid(),[s.Id],EditorTestData.Image(s)))]);
    }
    private static EditorSubmissionService Service(Engine engine,IEditorAdmissionInspector? inspector = null) => new(new JobQueue([engine]),new([new(engine.Operations[0].Id,"Resize",engine.Operations[0].IsGroup ? EditorSubmissionShape.Group : EditorSubmissionShape.PerRoot)]),inspector ?? new Inspector());

    [Fact]
    public async Task Typed_per_root_batch_freezes_distinct_documents_and_preserves_legacy_identity()
    {
        var batch = Batch(EditorTestData.PathFor("one.jpg"),EditorTestData.PathFor("two.tiff"));
        var engine = new Engine(Resize);
        Assert.Equal(0,engine.Executed);
        var jobs = await Service(engine).SubmitAsync(batch,default);
        Assert.Equal(2,jobs.Count);
        await Task.WhenAll(jobs.Select(j => j.Completion));
        Assert.All(jobs,j => Assert.Equal(ConversionJobState.Succeeded,j.State));
        Assert.Same(Resize,jobs[0].Request.Operation);
        Assert.Equal(batch.Items[1].Document,jobs[1].Request.Editor);
        Assert.NotEqual(jobs[0].Request.Editor,jobs[1].Request.Editor);
        batch = batch with { Items = [batch.Items[0] with { Document = batch.Items[0].Document with { Export = batch.Items[0].Document.Export with { OutputStem = "changed" } } }] };
        Assert.Equal("one-edited",jobs[0].Request.Editor!.Export.OutputStem);
        Assert.Equal(20,jobs[0].Compression!.ActualBytes);
    }

    [Fact]
    public async Task Grouped_document_queues_once()
    {
        var batch = Batch(EditorTestData.PathFor("one.pdf"),EditorTestData.PathFor("two.pdf"));
        var operation = new OperationDescriptor("pdf.merge","Merge",OperationMode.Tools,".pdf",true,false);
        var doc = batch.Items[0].Document with { Sources = batch.InitialSources, Recipe = new PdfRecipe([..batch.InitialSources.Select(s => new PdfPageRef(Guid.NewGuid(),s.Id,1,0))],PdfOutputMode.Document,300,PdfCompressionMode.PreserveStructure,85,[]) };
        batch = batch with { Request = batch.Request with { Operation = operation }, Items = [new(Guid.NewGuid(),batch.IncludedRootIds,doc)] };
        var jobs = await Service(new(operation)).SubmitAsync(batch,default);
        Assert.Equal(2,Assert.Single(jobs).Request.Inputs.Count);
    }

    [Fact]
    public async Task Explicit_import_is_not_initial_drag_payload()
    {
        var batch = Batch(EditorTestData.PathFor("one.png"));
        var added = EditorTestData.Source(EditorTestData.PathFor("added.png"));
        var good = batch with { Imports = [new(added,EditorImportRole.Root)], IncludedRootIds = [..batch.IncludedRootIds,added.Id], Items = [..batch.Items,new(Guid.NewGuid(),[added.Id],EditorTestData.Image(added))] };
        Assert.Equal(2,(await Service(new(Resize)).SubmitAsync(good,default)).Count);
        await Assert.ThrowsAsync<ArgumentException>(() => Service(new(Resize)).SubmitAsync(good with { InitialSources = [..batch.InitialSources,added], Imports = [] },default));
        Assert.Single(batch.Request.InitialPaths);
    }

    [Fact]
    public async Task Root_coverage_rejects_missing_duplicate_or_auxiliary_roots()
    {
        var batch = Batch(EditorTestData.PathFor("one.png"),EditorTestData.PathFor("two.png"));
        var service = Service(new(Resize));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(batch with { Items = [batch.Items[0]] },default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(batch with { Items = [batch.Items[0],batch.Items[0]] },default));
        var added = EditorTestData.Source(EditorTestData.PathFor("extra.png"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(batch with { Imports = [new(added,EditorImportRole.Auxiliary)], IncludedRootIds = [..batch.IncludedRootIds,added.Id], Items = [..batch.Items,new(Guid.NewGuid(),[added.Id],EditorTestData.Image(added))] },default));
    }

    [Fact]
    public async Task Included_capture_or_inspection_error_blocks_export_until_reload_or_explicit_exclusion()
    {
        var batch = Batch(EditorTestData.PathFor("one.png"),EditorTestData.PathFor("bad.png"));
        var engine = new Engine(Resize);
        var service = Service(engine,new Inspector((request,ct) => Task.FromResult<ImmutableArray<ValidationIssue>>(request.Inputs.Contains(EditorTestData.PathFor("bad.png")) ? [new("Source","Unreadable image")] : [])));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(batch,default));
        Assert.Equal(0,engine.Executed);
        var jobs = await service.SubmitAsync(batch with { ExcludedInitialPaths = [EditorTestData.PathFor("bad.png")],IncludedRootIds = [batch.IncludedRootIds[0]],Items = [batch.Items[0]] },default);
        Assert.Single(jobs);
    }

    [Fact]
    public async Task Uncaptured_initial_path_requires_explicit_exclusion_record()
    {
        var batch = Batch(EditorTestData.PathFor("one.png"));
        batch = batch with { Request = batch.Request with { InitialPaths = [EditorTestData.PathFor("one.png"),EditorTestData.PathFor("missing.png")] } };
        var service = Service(new(Resize));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(batch,default));
        Assert.Single(await service.SubmitAsync(batch with { ExcludedInitialPaths = [EditorTestData.PathFor("missing.png")] },default));
    }

    [Fact]
    public async Task Post_admission_independent_failure_does_not_cancel_siblings()
    {
        var jobs = await Service(new(Resize)).SubmitAsync(Batch(EditorTestData.PathFor("fail.png"),EditorTestData.PathFor("okay.png")),default);
        await Task.WhenAll(jobs.Select(j => j.Completion));
        Assert.Equal(ConversionJobState.Failed,jobs[0].State);
        Assert.Equal(ConversionJobState.Succeeded,jobs[1].State);
    }

    [Fact]
    public async Task Invalid_shared_user_field_queues_nothing()
    {
        var batch = Batch(EditorTestData.PathFor("one.png"),EditorTestData.PathFor("two.png"));
        var engine = new Engine(Resize);
        var bad = batch.Items[1] with { Document = batch.Items[1].Document with { Export = batch.Items[1].Document.Export with { OutputStem = "CON" } } };
        await Assert.ThrowsAsync<ArgumentException>(() => Service(engine).SubmitAsync(batch with { Items = [batch.Items[0],bad] },default));
        Assert.Equal(0,engine.Executed);
    }

    [Fact]
    public async Task Retry_keeps_frozen_identity_and_checks_inspection_before_admission()
    {
        var service = Service(new(Resize));
        var original = Assert.Single(await service.SubmitAsync(Batch(EditorTestData.PathFor("one.png")),default));
        var retry = await service.RetryAsync(original.Request,EditorTestData.PathFor("export"),default);
        Assert.Equal(original.Request.Editor!.Recipe,retry.Request.Editor!.Recipe);
        Assert.Equal(original.Request.Inputs,retry.Request.Inputs);
        Assert.Equal(EditorTestData.PathFor("export"),retry.Request.Editor.Export.DestinationDirectory);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RetryAsync(original.Request with { Editor = null },null,default));
    }

    [Fact]
    public async Task Cancellation_before_admission_queues_nothing()
    {
        using var cancellation = new CancellationTokenSource();
        var engine = new Engine(Resize);
        var service = Service(engine,new Inspector((request,ct) => { cancellation.Cancel(); return Task.FromResult(ImmutableArray<ValidationIssue>.Empty); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SubmitAsync(Batch(EditorTestData.PathFor("one.png"),EditorTestData.PathFor("two.png")),cancellation.Token));
        Assert.Equal(0,engine.Executed);
    }

    [Fact]
    public async Task Legacy_scalar_inspection_partial_success_unchanged()
    {
        var service = new SubmissionService(new JobQueue([new ScalarEngine()]),(draft,ct) => Task.FromResult<IReadOnlyList<InputInspection>>([new(draft.Inputs[0],null,null,"bad"),new(draft.Inputs[1],null,null,null)]));
        var jobs = await service.SubmitAsync(Resize,[EditorTestData.PathFor("a.png"),EditorTestData.PathFor("b.png")],(draft,ct) => Task.FromResult<SubmissionChoice?>(new(draft.Inputs,new() { Width = 100 },null)));
        await Task.WhenAll(jobs.Select(j => j.Completion));
        Assert.Equal(ConversionJobState.Failed,jobs[0].State); Assert.Equal(ConversionJobState.Succeeded,jobs[1].State);
    }
    private sealed class ScalarEngine : IConversionEngine
    {
        public IReadOnlyList<OperationDescriptor> Operations => [Resize];
        public bool CanExecute(OperationRequest request) => request.Editor is null;
        public Task<ConversionResult> ExecuteAsync(OperationRequest request,IProgress<ConversionProgress>? progress,CancellationToken ct) => Task.FromResult(new ConversionResult(["done"]));
    }
}
