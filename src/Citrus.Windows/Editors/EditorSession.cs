using Citrus.Core.Editing;

namespace Citrus.Windows.Editors;

/// <summary>Publication gate only. The host owns its renderer; admitted jobs own a separate renderer.</summary>
public sealed class EditorSession(IEditorRenderer renderer)
{
    private readonly object gate = new();
    private EditorDocument? document;
    private PreviewWork? pending;
    private long generation = -1, epoch;
    private bool closed;
    public void Update(EditorDocument next)
    {
        PreviewWork? previous;
        lock (gate)
        {
            if (closed) return;
            document = next; epoch++;
            previous = pending; pending = null;
        }
        previous?.Cancel();
    }
    public async Task<PreviewFrame?> PreviewAsync(PreviewRequest request, CancellationToken ct)
    {
        EditorDocument captured; PreviewWork work; PreviewWork? previous; long version;
        lock (gate)
        {
            if (closed || document is null || request.Generation <= generation) return null;
            generation = request.Generation; captured = document; version = epoch;
            previous = pending; pending = work = new(ct);
        }
        try
        {
            // State is authoritative before arbitrary synchronous cancellation callbacks can reenter.
            // No session lock is held while invoking those callbacks.
            previous?.Cancel();
            work.Token.ThrowIfCancellationRequested();
            var frame = await renderer.RenderPreviewAsync(captured, request, work.Token).ConfigureAwait(false);
            lock (gate)
                return !closed && !work.Token.IsCancellationRequested && epoch == version && document?.Revision == captured.Revision &&
                    generation == request.Generation && frame.Revision == captured.Revision && frame.Generation == request.Generation ? frame : null;
        }
        catch (OperationCanceledException) when (work.Token.IsCancellationRequested) { return null; }
        finally
        {
            lock (gate) { if (ReferenceEquals(pending, work)) pending = null; }
            work.Complete();
        }
    }
    public void Close()
    {
        PreviewWork? previous;
        lock (gate)
        {
            if (closed) return;
            closed = true; epoch++; document = null;
            previous = pending; pending = null;
        }
        previous?.Cancel();
    }
    /// <summary>Completion may race cancellation or run inside a token callback. Dispose only after both finish.</summary>
    private sealed class PreviewWork
    {
        private readonly object lifecycle = new();
        private readonly CancellationTokenSource source;
        private int cancellations;
        private bool complete;
        public CancellationToken Token { get; }
        public PreviewWork(CancellationToken ct) { source = CancellationTokenSource.CreateLinkedTokenSource(ct); Token = source.Token; }
        public void Cancel()
        {
            lock (lifecycle) { if (complete) return; cancellations++; }
            try { source.Cancel(); }
            finally { lock (lifecycle) { cancellations--; if (complete && cancellations == 0) source.Dispose(); } }
        }
        public void Complete()
        {
            lock (lifecycle) { complete = true; if (cancellations == 0) source.Dispose(); }
        }
    }
}
