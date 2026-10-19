using System.Collections.Immutable;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Windows.Editors;

namespace Citrus.Windows.Tests;

public sealed class EditorSessionTests
{
    [Fact]
    public async Task Latest_generation_and_revision_win_even_if_renderer_ignores_cancellation()
    {
        var renderer = new DelayedRenderer(); var session = new EditorSession(renderer);
        session.Update(Document(1));
        var old = session.PreviewAsync(Request(1), default);
        var latest = session.PreviewAsync(Request(2), default);
        renderer.Complete(1, 1); Assert.Null(await old);
        renderer.Complete(2, 1); Assert.NotNull(await latest);
        var changed = session.PreviewAsync(Request(3), default);
        session.Update(Document(2)); renderer.Complete(3, 1); Assert.Null(await changed);
    }
    [Fact]
    public async Task Close_rejects_late_results_and_does_not_own_export_renderer()
    {
        var renderer = new DelayedRenderer(); var session = new EditorSession(renderer);
        session.Update(Document(1)); var pending = session.PreviewAsync(Request(1), default);
        session.Close(); renderer.Complete(1, 1); Assert.Null(await pending);
        Assert.Null(await session.PreviewAsync(Request(2), default));
    }
    [Fact]
    public async Task Wrong_frame_identity_is_rejected()
    {
        var renderer = new DelayedRenderer(); var session = new EditorSession(renderer);
        session.Update(Document(5)); var pending = session.PreviewAsync(Request(1), default);
        renderer.Complete(1, 4); Assert.Null(await pending);
    }
    private static PreviewRequest Request(long generation) => new(generation, new(512, 512), null, null, null);
    internal static EditorDocument Document(long revision) => new(1, revision, [], [], null!, null!);
    private sealed class DelayedRenderer : IEditorRenderer
    {
        private readonly Dictionary<long, TaskCompletionSource<PreviewFrame>> pending = [];
        public Task<PreviewFrame> RenderPreviewAsync(EditorDocument document, PreviewRequest request, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<PreviewFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(request.Generation, completion); return completion.Task;
        }
        public void Complete(long generation, long revision) => pending[generation].SetResult(new(revision, generation, new(1, 1), 4, [0,0,0,255], null, null, []));
        public Task<StagedExport> RenderExportAsync(EditorDocument d, string p, IProgress<ConversionProgress>? progress, CancellationToken ct) => throw new NotSupportedException();
    }
}
