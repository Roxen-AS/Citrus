using System.Collections.Immutable;
using System.Windows;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Windows.Editors;

namespace Citrus.Windows.Tests;

public sealed class EditorWorkflowRegistryTests
{
    private static readonly OperationDescriptor Tool = new("audio.bleep", "Bleep", OperationMode.Tools, ".wav", false, true);
    private static readonly EditorRouteRegistry Routes = new([new(Tool.Id, "Bleep", EditorSubmissionShape.PerRoot)]);
    private sealed class Inspector : IEditorAdmissionInspector
    {
        public Task<ImmutableArray<ValidationIssue>> ValidateAsync(OperationRequest request, CancellationToken ct) => Task.FromResult(ImmutableArray<ValidationIssue>.Empty);
    }
    [Fact]
    public async Task Registered_tool_gets_its_correct_host_without_losing_ordered_payload_or_origin()
    {
        var registry = new EditorWorkflowRegistry(new JobQueue([]));
        EditorOpenRequest? captured = null;
        registry.Register([Tool], Routes, new Inspector(), (request, _, _) => { captured = request; return Task.FromResult<EditorSubmissionBatch?>(null); });
        Assert.True(registry.Contains(Tool.Id));
        var result = await registry.OpenAndSubmitAsync(new(Tool, ["second.wav", "first.wav"]), new(), null, SubmissionOrigin.Wheel, null, default);
        Assert.Empty(result);
        Assert.NotNull(captured);
        Assert.Equal(new[] { "second.wav", "first.wav" }, captured.InitialPaths.ToArray());
        Assert.Equal("Bleep", captured.EntryTool);
        Assert.Equal(SubmissionOrigin.Wheel, captured.Origin);
    }
    [Fact]
    public async Task Cancellation_before_open_does_not_construct_a_host()
    {
        var registry = new EditorWorkflowRegistry(new JobQueue([]));
        int opened = 0;
        registry.Register([Tool], Routes, new Inspector(), (_, _, _) => { opened++; return Task.FromResult<EditorSubmissionBatch?>(null); });
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.OpenAndSubmitAsync(new(Tool, ["file.wav"]), new(), null, SubmissionOrigin.Home, null, cancellation.Token));
        Assert.Equal(0, opened);
    }
    [Fact]
    public void Duplicate_hosts_are_rejected_instead_of_silently_overriding_a_family()
    {
        var registry = new EditorWorkflowRegistry(new JobQueue([]));
        Task<EditorSubmissionBatch?> Open(EditorOpenRequest _, Window? owner, CancellationToken ct) => Task.FromResult<EditorSubmissionBatch?>(null);
        registry.Register([Tool], Routes, new Inspector(), Open);
        Assert.Throws<ArgumentException>(() => registry.Register([Tool], Routes, new Inspector(), Open));
    }
}
