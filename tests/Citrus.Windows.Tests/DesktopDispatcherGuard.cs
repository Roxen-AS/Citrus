using System.Windows.Threading;

namespace Citrus.Windows.Tests;

/// <summary>A timed-out action invalidates this dispatcher fixture; later tests must not overlap it.</summary>
internal sealed class DesktopDispatcherGuard
{
    private Exception? timeoutFailure;

    public async Task RunAsync(Dispatcher dispatcher, Func<Task> action, TimeSpan timeout)
    {
        if (Volatile.Read(ref timeoutFailure) is { } previous)
            throw new InvalidOperationException("Desktop fixture is unavailable after an earlier dispatcher timeout.", previous);
        var operation = dispatcher.InvokeAsync(action);
        // Unwrap before waiting: both a blocked invocation queue and an incomplete async action are bounded.
        var execution = operation.Task.Unwrap();
        try { await execution.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException error) when (!execution.IsCompleted)
        {
            Interlocked.CompareExchange(ref timeoutFailure, error, null);
            operation.Abort(); // Prevent a queued test from starting after its caller has already failed.
            // An executing action cannot be forcibly stopped. Observe its eventual failure, and reject later actions.
            _ = execution.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new TimeoutException($"Desktop dispatcher/action exceeded {timeout.TotalSeconds:g} seconds; invocation status {operation.Status}. Fixture quarantined.", error);
        }
    }
}
