using System.Windows.Threading;
using System.Runtime.ExceptionServices;

namespace Citrus.Windows.Tests;

/// <summary>A timed-out action or fatal exit invalidates this dispatcher fixture; later tests must not overlap it.</summary>
internal sealed class DesktopDispatcherGuard
{
    private Exception? terminalFailure;

    public async Task RunAsync(Dispatcher dispatcher, Func<Task> action, TimeSpan timeout, Task<Exception>? fatalExit = null)
    {
        if (Volatile.Read(ref terminalFailure) is { } previous)
            throw new InvalidOperationException("Desktop fixture is unavailable after an earlier dispatcher timeout or fatal exit.", previous);
        if (fatalExit?.IsCompletedSuccessfully == true)
        {
            var failure = await fatalExit.ConfigureAwait(false);
            Interlocked.CompareExchange(ref terminalFailure, failure, null);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        var operation = dispatcher.InvokeAsync(action);
        // Unwrap before waiting: both a blocked invocation queue and an incomplete async action are bounded.
        var execution = operation.Task.Unwrap();
        Task completion = fatalExit is null ? execution : Task.WhenAny(execution, fatalExit);
        try { await completion.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException error) when (!completion.IsCompleted)
        {
            Interlocked.CompareExchange(ref terminalFailure, error, null);
            operation.Abort(); // Prevent a queued test from starting after its caller has already failed.
            // An executing action cannot be forcibly stopped. Observe its eventual failure, and reject later actions.
            ObserveFault(execution);
            throw new TimeoutException($"Desktop dispatcher/action exceeded {timeout.TotalSeconds:g} seconds; invocation status {operation.Status}. Fixture quarantined.", error);
        }
        // Keep propagation outside the deadline catch: an original TimeoutException is still the original error.
        if (fatalExit?.IsCompletedSuccessfully == true)
        {
            var failure = await fatalExit.ConfigureAwait(false);
            Interlocked.CompareExchange(ref terminalFailure, failure, null);
            operation.Abort();
            ObserveFault(execution);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        await execution.ConfigureAwait(false);
    }

    private static void ObserveFault(Task task) => _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
