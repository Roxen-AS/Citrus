using System.Reflection;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class GlassRenderDelayTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cancellation_releases_the_wait_and_allows_the_same_timer_to_be_reused(bool fallback)
    {
        using var delay = Create(fallback);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var before = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(delay, 5000, canceled.Token));
        Assert.Equal(canceled.Token, before.CancellationToken);
        using var active = new CancellationTokenSource();
        Task pending = Wait(delay, 5000, active.Token);
        Assert.False(pending.IsCompleted);
        active.Cancel();
        var during = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(active.Token, during.CancellationToken);
        await Wait(delay, 1).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task One_timer_rejects_overlapping_waits_without_disturbing_its_owner(bool fallback)
    {
        using var delay = Create(fallback);
        using var cancellation = new CancellationTokenSource();
        Task pending = Wait(delay, 5000, cancellation.Token);
        try
        {
            Assert.False(pending.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Wait(delay, 1));
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        await Wait(delay, 1).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Disposal_finishes_a_pending_wait_and_rejects_further_work(bool fallback)
    {
        var delay = Create(fallback);
        try
        {
            Task pending = Wait(delay, 5000);
            Assert.False(pending.IsCompleted);
            delay.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pending).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => Wait(delay, 1));
            delay.Dispose();
        }
        finally { delay.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cancellation_and_signaling_races_do_not_corrupt_the_next_wait(bool fallback)
    {
        using var delay = Create(fallback);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            using var cancellation = new CancellationTokenSource();
            Task pending = Wait(delay, 1, cancellation.Token);
            await Task.Yield();
            cancellation.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException error) { Assert.Equal(cancellation.Token, error.CancellationToken); }
            await Wait(delay, 1).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Explicit_fallback_requires_no_native_timer_and_completes_on_a_worker()
    {
        using var delay = Create(true);
        var available = DelayType().GetProperty("HighResolutionAvailable", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(available);
        Assert.False(Assert.IsType<bool>(available.GetValue(delay)));
        await Task.Run(() => Wait(delay, 1)).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Type DelayType()
    {
        var type = typeof(GlassScene).Assembly.GetType("Citrus.Windows.Presentation.GlassRenderDelay");
        Assert.NotNull(type);
        return type;
    }
    private static IDisposable Create(bool fallback) => Assert.IsAssignableFrom<IDisposable>(Activator.CreateInstance(DelayType(),
        BindingFlags.Instance | BindingFlags.NonPublic, null, [fallback], null));
    private static Task Wait(IDisposable delay, int milliseconds, CancellationToken cancellation = default)
    {
        var method = DelayType().GetMethod("WaitAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(typeof(ValueTask), method.ReturnType);
        return Assert.IsType<ValueTask>(method.Invoke(delay, [milliseconds, cancellation])).AsTask();
    }
}
