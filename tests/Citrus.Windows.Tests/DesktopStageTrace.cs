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
    private readonly Thread ownerThread;

    public DesktopStageTrace(string name, Dispatcher dispatcher)
    {
        ownerThread = Thread.CurrentThread;
        string directory = Path.Combine(Environment.CurrentDirectory, "TestResults", "diagnostics");
        Directory.CreateDirectory(directory);
        stream = new FileStream(Path.Combine(directory, name + ".jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(stream, leaveOpen: true);
        Mark("trace.started");
        probe = ProbeAsync(dispatcher);
    }

    public void Mark(string stage, object? details = null)
    {
        lock (gate)
        {
            if (disposed || count >= 256) return;
            writer.WriteLine(JsonSerializer.Serialize(new { sequence = ++count, utc = DateTimeOffset.UtcNow,
                thread = Environment.CurrentManagedThreadId, stage, details }));
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
            Mark("watchdog.fired.off-dispatcher", DispatcherState(ownerThread, dispatcher));
            foreach (var priority in new[] { DispatcherPriority.Send, DispatcherPriority.Normal, DispatcherPriority.Render, DispatcherPriority.ApplicationIdle })
            {
                stop.Token.ThrowIfCancellationRequested();
                Mark($"watchdog.{priority}.queued");
                queued.Add(dispatcher.InvokeAsync(() => Mark($"watchdog.{priority}.executed"), priority, stop.Token));
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stop.Token).ConfigureAwait(false);
            foreach (var operation in queued) Mark($"watchdog.{operation.Priority}.status.{operation.Status}");
            Mark("watchdog.thread-state", DispatcherState(ownerThread, dispatcher));
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

    internal static object DispatcherState(Thread thread, Dispatcher dispatcher) => new
    {
        managedThreadId = thread.ManagedThreadId, thread.IsAlive, threadState = thread.ThreadState.ToString(),
        dispatcher.HasShutdownStarted, dispatcher.HasShutdownFinished
    };

    internal static void RecordDispatcherExit(Thread thread, Dispatcher dispatcher, bool normalReturn, bool disposalRequested, Exception? error)
    {
        string directory = Path.Combine(Environment.CurrentDirectory, "TestResults", "diagnostics");
        Directory.CreateDirectory(directory);
        using var output = new FileStream(Path.Combine(directory, "desktop-dispatcher-exit.json"), FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(output, new { utc = DateTimeOffset.UtcNow, normalReturn, disposalRequested,
            state = DispatcherState(thread, dispatcher), exception = error?.ToString(),
            note = "Snapshot taken inside the dispatcher thread's terminal handler; watchdog independently records subsequent liveness." });
        output.Flush(flushToDisk: true);
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
