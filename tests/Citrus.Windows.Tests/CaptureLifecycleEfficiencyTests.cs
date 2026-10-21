using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class CaptureLifecycleEfficiencyTests
{
    [Fact]
    public async Task Independent_monitor_creation_is_not_blocked_by_another_monitors_OS_startup()
    {
        using var creating = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var registry = new DesktopGlassCapturePool.Registry((x, _) => x < 100 ? 1 : 2, (monitor, _, _) =>
        {
            if (monitor == 1) { creating.Set(); release.Wait(TimeSpan.FromSeconds(5)); }
            return new Source();
        });
        Task<DesktopGlassCapturePool.Lease?> first = Task.Run(() => registry.TryAcquire(1, 1));
        Assert.True(creating.Wait(TimeSpan.FromSeconds(2)));
        Task<DesktopGlassCapturePool.Lease?> second = Task.Run(() => registry.TryAcquire(101, 1));
        try { Assert.Same(second, await Task.WhenAny(second, Task.Delay(250))); }
        finally
        {
            release.Set();
            (await first)?.Dispose(); (await second)?.Dispose();
        }
    }

    [Fact]
    public async Task A_replacement_monitor_source_waits_for_its_predecessors_native_shutdown()
    {
        using var retiring = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int creations = 0;
        var registry = new DesktopGlassCapturePool.Registry((_, _) => 1, (_, _, _) =>
            Interlocked.Increment(ref creations) == 1 ? new Source(() => { retiring.Set(); release.Wait(TimeSpan.FromSeconds(5)); }) : new Source());
        var first = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(1, 1));
        Task stop = Task.Run(first.Dispose);
        Assert.True(retiring.Wait(TimeSpan.FromSeconds(2)));
        Task<DesktopGlassCapturePool.Lease?> replacement = Task.Run(() => registry.TryAcquire(1, 1));
        try
        {
            await Task.Delay(100);
            Assert.Equal(1, Volatile.Read(ref creations));
            Assert.False(replacement.IsCompleted);
        }
        finally { release.Set(); await stop; (await replacement)?.Dispose(); }
        Assert.Equal(2, creations);
    }

    [Fact]
    public async Task Registry_retirement_completion_tracks_an_outstanding_shutdown_without_waiting_on_its_lock()
    {
        var property = typeof(DesktopGlassCapturePool.Registry).GetProperty("RetirementCompletion",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(property);
        using var retiring = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var registry = new DesktopGlassCapturePool.Registry((_, _) => 1, (_, _, _) => new Source(() =>
        { retiring.Set(); release.Wait(TimeSpan.FromSeconds(5)); }));
        var lease = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(1, 1));
        Task stop = Task.Run(lease.Dispose);
        Assert.True(retiring.Wait(TimeSpan.FromSeconds(2)));
        try
        {
            var completion = Assert.IsAssignableFrom<Task>(property.GetValue(registry));
            Assert.False(completion.IsCompleted);
            release.Set(); await stop; await completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { release.Set(); await stop; }
    }

    private sealed class Source(Action? stop = null) : DesktopGlassCapturePool.ISource
    {
        public bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int> useFrame)
        { useFrame(1, 2, 3, 100, 100, 0, 0); return true; }
        public void Dispose() => stop?.Invoke();
    }
}
