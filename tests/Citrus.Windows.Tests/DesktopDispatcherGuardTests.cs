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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fatal_exit_preserves_original_error_and_quarantines_the_fixture(bool originallyTimeout)
    {
        using var sta = new IsolatedDispatcher();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fatal = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard = new DesktopDispatcherGuard();
        var original = CaptureOriginalError(originallyTimeout);
        try
        {
            var run = guard.RunAsync(sta.Dispatcher, () => { entered.SetResult(); return pending.Task; }, TimeSpan.FromSeconds(2), fatal.Task);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            fatal.SetResult(original);
            var surfaced = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Same(original, surfaced);
            Assert.Contains(nameof(CaptureOriginalError), surfaced!.StackTrace);
            var quarantined = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.RunAsync(sta.Dispatcher,
                () => Task.CompletedTask, TimeSpan.FromSeconds(1), fatal.Task));
            Assert.Same(original, quarantined.InnerException);
        }
        finally { pending.TrySetResult(); }
    }

    [Fact]
    public async Task Already_reported_fatal_exit_does_not_enqueue_another_action()
    {
        using var sta = new IsolatedDispatcher();
        var original = CaptureOriginalError(false);
        bool executed = false;
        var surfaced = await Record.ExceptionAsync(() => new DesktopDispatcherGuard().RunAsync(sta.Dispatcher,
            () => { executed = true; return Task.CompletedTask; }, TimeSpan.FromSeconds(1), Task.FromResult(original)));
        Assert.Same(original, surfaced);
        Assert.False(executed);
    }

    private static Exception CaptureOriginalError(bool timeout)
    {
        try
        {
            if (timeout) throw new TimeoutException("Original dispatcher failure, not the guard deadline.");
            throw new InvalidOperationException("Original dispatcher failure.");
        }
        catch (Exception error) { return error; }
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
