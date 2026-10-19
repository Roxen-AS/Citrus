namespace Citrus.Core;

/// <summary>A session-only request and its outcome. Completion never faults; inspect State and Error.</summary>
public sealed class ConversionJob
{
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ConversionJobState state = ConversionJobState.Queued;
    private ConversionProgress? progress;
    private Exception? error;
    private IReadOnlyList<string> outputs = [];

    internal ConversionJob(OperationRequest request) => Request = request with { Inputs = Array.AsReadOnly(request.Inputs.ToArray()) };
    public OperationRequest Request { get; }
    public ConversionJobState State { get { lock (gate) return state; } }
    public ConversionProgress? Progress { get { lock (gate) return progress; } }
    public Exception? Error { get { lock (gate) return error; } }
    public IReadOnlyList<string> Outputs { get { lock (gate) return outputs; } }
    public Task Completion => completion.Task;
    public void Cancel() => cancellation.Cancel();
    internal CancellationToken Token => cancellation.Token;

    internal void SetState(ConversionJobState value, Exception? failure = null, IReadOnlyList<string>? results = null)
    {
        lock (gate)
        {
            state = value;
            error = failure;
            if (results is not null) outputs = Array.AsReadOnly(results.ToArray());
        }
    }

    internal bool Report(ConversionProgress value)
    {
        lock (gate)
        {
            if (state != ConversionJobState.Running) return false;
            progress = value;
            return true;
        }
    }

    internal void Complete() => completion.TrySetResult();
}

/// <summary>One request per job; the presentation layer splits ordinary batches into single-input requests.</summary>
public sealed class JobQueue
{
    private readonly IConversionEngine[] engines;
    private readonly SemaphoreSlim slots;

    public JobQueue(IEnumerable<IConversionEngine> engines, int maxConcurrency = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        this.engines = engines.ToArray();
        slots = new(maxConcurrency, maxConcurrency);
    }

    /// <summary>Raised on background workers; UI subscribers must dispatch updates to their UI thread.</summary>
    public event Action<ConversionJob>? JobChanged;

    public ConversionJob Enqueue(OperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var job = new ConversionJob(request);
        _ = Task.Run(() => RunAsync(job));
        return job;
    }

    private async Task RunAsync(ConversionJob job)
    {
        bool acquired = false;
        try
        {
            Notify(job);
            await slots.WaitAsync(job.Token).ConfigureAwait(false);
            acquired = true;
            job.Token.ThrowIfCancellationRequested();
            job.SetState(ConversionJobState.Running);
            Notify(job);
            var engine = engines.FirstOrDefault(engine =>
                engine.Operations.Any(operation => operation.Id == job.Request.Operation.Id) && engine.CanExecute(job.Request))
                ?? throw new NotSupportedException($"No installed engine supports operation '{job.Request.Operation.Id}' for these inputs.");
            var progress = new JobProgress(value => { if (job.Report(value)) Notify(job); });
            var result = await engine.ExecuteAsync(job.Request, progress, job.Token).ConfigureAwait(false);
            // Successful return owns the commit outcome; late cancellation must not hide user outputs.
            job.SetState(ConversionJobState.Succeeded, results: result.Outputs);
        }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested)
        {
            job.SetState(ConversionJobState.Canceled);
        }
        catch (Exception error)
        {
            job.SetState(ConversionJobState.Failed, error);
        }
        finally
        {
            if (acquired) slots.Release();
            Notify(job);
            job.Complete();
        }
    }

    private void Notify(ConversionJob job)
    {
        if (JobChanged is not { } handlers) return;
        foreach (Action<ConversionJob> handler in handlers.GetInvocationList())
        {
            // A presentation subscriber must not turn a successful conversion into a failure.
            try { handler(job); }
            catch (Exception) { }
        }
    }

    private sealed class JobProgress(Action<ConversionProgress> report) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => report(value);
    }
}
