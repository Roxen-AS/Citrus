using System.Windows.Threading;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class DesktopDispatcherGuardTests
{
    [Fact]
    public async Task Blocked_dispatch_queue_times_out_and_aborts_the_queued_action()
    {
        using var sta = new IsolatedDispatcher();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = sta.Dispatcher.InvokeAsync(() => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); });
        bool executed = false;
        var guard = new DesktopDispatcherGuard();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<TimeoutException>(() => guard.RunAsync(sta.Dispatcher,
                () => { executed = true; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(100)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => guard.RunAsync(sta.Dispatcher,
                () => Task.CompletedTask, TimeSpan.FromSeconds(1)));
        }
        finally { release.Set(); await blocker.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
        await sta.Dispatcher.InvokeAsync(() => { });
        Assert.False(executed);
    }

    [Fact]
    public async Task Incomplete_action_times_out_and_quarantines_later_actions()
    {
        using var sta = new IsolatedDispatcher();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard = new DesktopDispatcherGuard();
        try
        {
            var run = guard.RunAsync(sta.Dispatcher, () => { entered.SetResult(); return pending.Task; }, TimeSpan.FromMilliseconds(100));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<TimeoutException>(() => run);
            await Assert.ThrowsAsync<InvalidOperationException>(() => guard.RunAsync(sta.Dispatcher,
                () => Task.CompletedTask, TimeSpan.FromSeconds(1)));
        }
        finally { pending.TrySetResult(); }
    }

    private sealed class IsolatedDispatcher : IDisposable
    {
        private readonly Thread thread;
        public Dispatcher Dispatcher { get; }
        public IsolatedDispatcher()
        {
            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            thread = new Thread(() => { ready.SetResult(Dispatcher.CurrentDispatcher); Dispatcher.Run(); }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
        public void Dispose()
        {
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            if (!thread.Join(TimeSpan.FromSeconds(2))) throw new TimeoutException("Diagnostic test dispatcher did not stop.");
        }
    }
}
