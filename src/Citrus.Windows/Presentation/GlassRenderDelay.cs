using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Citrus.Windows.Presentation;

/// <summary>A worker-owned one-shot wait; it never changes the Windows timer resolution.</summary>
internal sealed class GlassRenderDelay : IDisposable
{
    private readonly object gate = new();
    private TimerWaitHandle? timer;
    private PendingWait? pending;
    private bool disposed;

    internal bool HighResolutionAvailable { get { lock (gate) return timer is not null; } }

    internal GlassRenderDelay(bool forceFallback = false)
    {
        if (forceFallback) return;
        try
        {
            // Unnamed, non-inheritable, synchronization timer. Windows 10 1803+.
            // Only SYNCHRONIZE and TIMER_MODIFY_STATE are needed.
            var handle = CreateWaitableTimerExW(0, null, 0x2, 0x00100002);
            if (handle.IsInvalid) handle.Dispose();
            else timer = new TimerWaitHandle(handle);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or NotSupportedException)
        { /* Ordinary cancellable delays remain available when this capability is absent. */ }
    }

    internal async ValueTask WaitAsync(int milliseconds, CancellationToken cancellation = default)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        cancellation.ThrowIfCancellationRequested();
        PendingWait operation;
        bool native = false;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (pending is not null) throw new InvalidOperationException("A render worker can own only one pending timer wait.");
            if (milliseconds == 0) return;
            operation = new PendingWait(cancellation);
            pending = operation;
            try
            {
                if (timer is not null)
                {
                    long dueTime = -checked((long)milliseconds * 10_000); // Relative 100-ns units.
                    if (SetWaitableTimerEx(timer.SafeWaitHandle, ref dueTime, 0, 0, 0, 0, 0))
                    {
                        operation.Registration = ThreadPool.RegisterWaitForSingleObject(timer,
                            static (state, _) => { ((PendingWait)state!).Completion.TrySetResult(); },
                            operation, Timeout.Infinite, executeOnlyOnce: true);
                        native = true;
                    }
                    else
                    {
                        timer.Dispose(); timer = null;
                    }
                }
                if (!native) operation.FallbackCancellation = new CancellationTokenSource();
            }
            catch
            {
                if (timer is not null) CancelWaitableTimer(timer.SafeWaitHandle);
                pending = null;
                operation.FallbackCancellation?.Dispose();
                throw;
            }
        }

        using var registration = cancellation.Register(static state => ((PendingWait)state!).Cancel(), operation);
        try
        {
            if (!native)
            {
                try
                {
                    await Task.Delay(milliseconds, operation.FallbackCancellation!.Token).ConfigureAwait(false);
                    operation.Completion.TrySetResult();
                }
                catch (OperationCanceledException)
                { /* The completion preserves the caller's token or disposal exception. */ }
            }
            await operation.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            // No callback touches the timer or the owner's next operation. Unregister
            // before making the timer reusable; a queued callback owns only this TCS.
            registration.Dispose();
            lock (gate)
            {
                operation.Registration?.Unregister(null);
                if (native && timer is not null) CancelWaitableTimer(timer.SafeWaitHandle);
                pending = null;
                if (disposed) { timer?.Dispose(); timer = null; }
            }
            operation.FallbackCancellation?.Dispose();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (pending is null) { timer?.Dispose(); timer = null; return; }
            pending.Completion.TrySetException(new ObjectDisposedException(nameof(GlassRenderDelay)));
            pending.FallbackCancellation?.Cancel();
            if (timer is not null) CancelWaitableTimer(timer.SafeWaitHandle);
            // WaitAsync unregisters its pending native wait before closing the handle.
        }
    }

    private sealed class PendingWait(CancellationToken cancellation)
    {
        internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal RegisteredWaitHandle? Registration;
        internal CancellationTokenSource? FallbackCancellation;
        internal void Cancel()
        {
            if (Completion.TrySetCanceled(cancellation)) FallbackCancellation?.Cancel();
        }
    }

    private sealed class TimerWaitHandle : WaitHandle
    {
        internal TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, ref long dueTime, int period,
        nint completion, nint argument, nint wakeContext, uint tolerableDelay);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelWaitableTimer(SafeWaitHandle timer);
}
