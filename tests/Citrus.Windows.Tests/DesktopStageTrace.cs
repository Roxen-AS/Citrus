using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Citrus.Windows.Tests;

/// <summary>Bounded, flushed test-owned diagnostics; never reads screen pixels or changes the scene.</summary>
internal sealed class DesktopStageTrace : IDisposable
{
    private readonly object gate = new();
    private readonly FileStream stream;
    private readonly StreamWriter writer;
    private readonly CancellationTokenSource stop = new();
    private readonly Task probe;
    private int count;
    private bool disposed;

    public DesktopStageTrace(string name, Dispatcher dispatcher)
    {
        string directory = Path.Combine(Environment.CurrentDirectory, "TestResults", "diagnostics");
        Directory.CreateDirectory(directory);
        stream = new FileStream(Path.Combine(directory, name + ".jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(stream, leaveOpen: true);
        Mark("trace.started");
        probe = ProbeAsync(dispatcher);
    }

    public void Mark(string stage)
    {
        lock (gate)
        {
            if (disposed || count >= 256) return;
            writer.WriteLine(JsonSerializer.Serialize(new { sequence = ++count, utc = DateTimeOffset.UtcNow,
                thread = Environment.CurrentManagedThreadId, stage }));
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    private async Task ProbeAsync(Dispatcher dispatcher)
    {
        var queued = new List<DispatcherOperation>();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stop.Token).ConfigureAwait(false);
            Mark("watchdog.fired.off-dispatcher");
            foreach (var priority in new[] { DispatcherPriority.Send, DispatcherPriority.Normal, DispatcherPriority.Render, DispatcherPriority.ApplicationIdle })
            {
                stop.Token.ThrowIfCancellationRequested();
                Mark($"watchdog.{priority}.queued");
                queued.Add(dispatcher.InvokeAsync(() => Mark($"watchdog.{priority}.executed"), priority, stop.Token));
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stop.Token).ConfigureAwait(false);
            foreach (var operation in queued) Mark($"watchdog.{operation.Priority}.status.{operation.Status}");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            foreach (var operation in queued) operation.Abort();
            // Observe callback failures even if an already-running callback finishes after probe cancellation.
            foreach (var operation in queued)
                _ = operation.Task.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        // Never block the dispatcher waiting for its own probe. Observe probe faults on a pool continuation.
        _ = probe.ContinueWith(task => { _ = task.Exception; stop.Dispose(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            writer.Dispose(); stream.Dispose();
        }
    }
}
