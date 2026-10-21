using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class StudioCaptureLeaseTests
{
    [Fact]
    public void Native_affinity_changes_invalidate_content_but_additional_references_do_not()
    {
        var api = new FakeAffinity(); api.Values[1] = 0;
        int changes = 0;
        var registry = new StudioCaptureExclusion.Registry(api, () => changes++);
        var first = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire([1]));
        var second = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire([1]));
        Assert.Equal(1, changes);
        first.Dispose();
        Assert.Equal(1, changes);
        second.Dispose();
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Capture_revision_discards_cached_content_and_rejects_compositor_frames_before_the_flush()
    {
        var barrier = new CaptureFrameBarrier();
        int discards = 0, flushes = 0;
        Assert.True(barrier.Refresh(1, () => discards++, () => { flushes++; return 100; }));
        Assert.False(barrier.Accept(99));
        Assert.True(barrier.Accept(100));
        Assert.False(barrier.Refresh(1, () => discards++, () => { flushes++; return 999; }));
        Assert.Equal(1, discards); Assert.Equal(1, flushes);
        Assert.True(barrier.Refresh(2, () => discards++, () => 200));
        Assert.False(barrier.Accept(199));
        Assert.True(barrier.Accept(201));
        Assert.Equal(2, discards);
    }

    [Fact]
    public void Newly_started_capture_accepts_its_initial_frame_without_restarting_the_session()
    {
        var barrier = new CaptureFrameBarrier();
        barrier.Initialize(3, 100);
        Assert.False(barrier.Refresh(3, () => throw new InvalidOperationException("Initial frame was unnecessarily discarded."),
            () => throw new InvalidOperationException("Initial capture was unnecessarily restarted.")));
        Assert.False(barrier.Accept(99));
        Assert.True(barrier.Accept(100));
        Assert.True(barrier.Refresh(4, () => { }, () => 200));
        Assert.False(barrier.Accept(100));
        Assert.True(barrier.Accept(200));
    }

    [Fact]
    public void Monitor_source_is_created_once_and_disposed_after_its_final_surface()
    {
        int creations = 0;
        var source = new FakeSource();
        var registry = new DesktopGlassCapturePool.Registry((_, _) => 10, (_, _, _) => { creations++; return source; });
        var first = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire(5, 5));
        var second = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire(25, 50));
        Assert.Equal(1, creations);
        first.Dispose();
        Assert.Equal(0, source.Disposals);
        second.Dispose();
        Assert.Equal(1, source.Disposals);
        second.Dispose();
        Assert.Equal(1, source.Disposals);
    }

    [Fact]
    public void Independent_monitors_have_independent_sources_and_failed_startup_is_not_cached()
    {
        var firstSource = new FakeSource(); var secondSource = new FakeSource();
        int attempts = 0;
        var registry = new DesktopGlassCapturePool.Registry((x, _) => x < 100 ? 1 : 2,
            (monitor, _, _) => ++attempts == 1 ? null : monitor == 1 ? firstSource : secondSource);
        Assert.Null(registry.TryAcquire(1, 1));
        using var first = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(1, 1));
        using var second = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(101, 1));
        Assert.Equal(3, attempts);
        first.Dispose();
        Assert.Equal(1, firstSource.Disposals);
        Assert.Equal(0, secondSource.Disposals);
    }

    [Fact]
    public async Task Multiple_surfaces_serialize_render_callbacks_on_the_shared_immediate_context()
    {
        var source = new FakeSource();
        var registry = new DesktopGlassCapturePool.Registry((_, _) => 1, (_, _, _) => source);
        using var first = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(1, 1));
        using var second = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(10, 10));
        int concurrent = 0, maximum = 0, calls = 0;
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            (index % 2 == 0 ? first : second).TryUseLatestFrame((_, _, _, _, _, _, _) =>
            {
                int active = Interlocked.Increment(ref concurrent);
                int observed;
                do { observed = Volatile.Read(ref maximum); }
                while (active > observed && Interlocked.CompareExchange(ref maximum, active, observed) != observed);
                Thread.SpinWait(40_000);
                Interlocked.Increment(ref calls);
                Interlocked.Decrement(ref concurrent);
            }))));
        Assert.Equal(12, calls);
        Assert.Equal(1, maximum);
    }

    [Fact]
    public void Disposed_surface_cannot_invoke_a_monitor_context_still_leased_by_another_surface()
    {
        var source = new FakeSource();
        var registry = new DesktopGlassCapturePool.Registry((_, _) => 1, (_, _, _) => source);
        var first = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(1, 1));
        using var second = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(10, 10));
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.TryUseLatestFrame((_, _, _, _, _, _, _) => { }));
        Assert.True(second.TryUseLatestFrame((_, _, _, _, _, _, _) => { }));
        Assert.Equal(0, source.Disposals);
    }

    [Fact]
    public void Overlapping_leases_restore_original_affinity_only_after_final_release()
    {
        var api = new FakeAffinity();
        api.Values[1] = 1;
        var registry = new StudioCaptureExclusion.Registry(api);
        var first = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire([1]));
        var second = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire([1]));
        Assert.Equal(0x11u, api.Values[1]);
        Assert.Single(api.Writes);
        first.Dispose();
        Assert.Equal(0x11u, api.Values[1]);
        second.Dispose();
        Assert.Equal(1u, api.Values[1]);
        Assert.Equal(2, api.Writes.Count);
        second.Dispose();
        Assert.Equal(2, api.Writes.Count);
    }

    [Fact]
    public void Failed_acquisition_rolls_back_its_new_windows_without_releasing_an_existing_lease()
    {
        var api = new FakeAffinity();
        api.Values[1] = 1; api.Values[2] = 0; api.Values[3] = 0;
        var registry = new StudioCaptureExclusion.Registry(api);
        using var existing = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire([1]));
        api.FailSet = 3;
        Assert.Null(registry.TryAcquire([1, 2, 3]));
        Assert.Equal(0x11u, api.Values[1]);
        Assert.Equal(0u, api.Values[2]);
        existing.Dispose();
        Assert.Equal(1u, api.Values[1]);
    }

    [Fact]
    public void Duplicate_windows_have_one_reference_and_one_restore()
    {
        var api = new FakeAffinity(); api.Values[1] = 0;
        var registry = new StudioCaptureExclusion.Registry(api);
        var lease = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire([1, 1, 1]));
        Assert.Single(api.Writes);
        lease.Dispose();
        Assert.Equal(0u, api.Values[1]);
        Assert.Equal(2, api.Writes.Count);
    }

    [Fact]
    public void Invalid_or_foreign_windows_are_rejected_before_affinity_mutation()
    {
        var api = new FakeAffinity(); api.Values[1] = 0; api.Values[2] = 0; api.Foreign.Add(2);
        var registry = new StudioCaptureExclusion.Registry(api);
        Assert.Null(registry.TryAcquire([]));
        Assert.Null(registry.TryAcquire([0]));
        Assert.Null(registry.TryAcquire([1, 2]));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public void Failed_affinity_readback_is_rolled_back_and_never_returned_as_a_lease()
    {
        var api = new FakeAffinity(); api.Values[1] = 1; api.IgnoreSet = 1;
        var registry = new StudioCaptureExclusion.Registry(api);
        Assert.Null(registry.TryAcquire([1]));
        Assert.Equal(1u, api.Values[1]);
    }

    [Fact]
    public void Window_destroyed_or_transferred_before_release_is_not_modified()
    {
        var api = new FakeAffinity(); api.Values[1] = 0;
        var registry = new StudioCaptureExclusion.Registry(api);
        var lease = Assert.IsAssignableFrom<IDisposable>(registry.TryAcquire([1]));
        api.Foreign.Add(1);
        lease.Dispose();
        Assert.Single(api.Writes);
    }

    private sealed class FakeAffinity : StudioCaptureExclusion.IAffinityApi
    {
        internal readonly Dictionary<nint, uint> Values = [];
        internal readonly HashSet<nint> Foreign = [];
        internal readonly List<(nint Window, uint Value)> Writes = [];
        internal nint FailSet, IgnoreSet;
        public bool IsOwnedTopLevelWindow(nint window) => Values.ContainsKey(window) && !Foreign.Contains(window);
        public bool TryGet(nint window, out uint affinity) => Values.TryGetValue(window, out affinity);
        public bool TrySet(nint window, uint affinity)
        {
            Writes.Add((window, affinity));
            if (window == FailSet) return false;
            if (window != IgnoreSet) Values[window] = affinity;
            return true;
        }
    }

    private sealed class FakeSource : DesktopGlassCapturePool.ISource
    {
        internal int Disposals;
        public bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int> useFrame)
        { useFrame(1, 2, 3, 100, 100, 0, 0); return true; }
        public void Dispose() => Disposals++;
    }
}
