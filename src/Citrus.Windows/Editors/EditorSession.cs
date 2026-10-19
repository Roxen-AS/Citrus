using Citrus.Core.Editing;

namespace Citrus.Windows.Editors;

/// <summary>Publication gate only. The host owns its renderer; admitted jobs own a separate renderer.</summary>
public sealed class EditorSession(IEditorRenderer renderer)
{
    private readonly object gate = new();
    private EditorDocument? document;
    private CancellationTokenSource? pending;
    private long generation = -1, epoch;
    private bool closed;
    public void Update(EditorDocument next)
    {
        lock (gate)
        {
            if (closed) return;
            document = next; epoch++;
            pending?.Cancel();
        }
    }
    public async Task<PreviewFrame?> PreviewAsync(PreviewRequest request, CancellationToken ct)
    {
        EditorDocument captured; CancellationTokenSource work; long version;
        lock (gate)
        {
            if (closed || document is null || request.Generation <= generation) return null;
            pending?.Cancel(); generation = request.Generation; captured = document; version = epoch;
            pending = work = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }
        try
        {
            var frame = await renderer.RenderPreviewAsync(captured, request, work.Token).ConfigureAwait(false);
            lock (gate)
                return !closed && !work.IsCancellationRequested && epoch == version && document?.Revision == captured.Revision &&
                    generation == request.Generation && frame.Revision == captured.Revision && frame.Generation == request.Generation ? frame : null;
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { return null; }
        finally
        {
            lock (gate) { if (ReferenceEquals(pending, work)) pending = null; work.Dispose(); }
        }
    }
    public void Close() { lock (gate) { if (closed) return; closed = true; epoch++; pending?.Cancel(); document = null; } }
}
