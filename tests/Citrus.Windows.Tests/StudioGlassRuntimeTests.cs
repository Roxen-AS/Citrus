using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class StudioGlassRuntimeTests
{
    [Fact]
    public void Returned_allocations_are_cleared_before_reuse_and_keep_at_most_two_idle_entries()
    {
        using var cache = CreateCache(2, 64, allocation => allocation.Clear());
        Allocation first = new(20), second = new(20), third = new(20);
        Assert.True(cache.Return("first", first));
        Assert.True(cache.Return("second", second));
        Assert.Equal(1, first.Clears);
        Assert.True(cache.Return("third", third));
        Assert.Equal(2, cache.IdleCount);
        Assert.Equal(40, cache.IdleBytes);
        Assert.Equal(1, first.Disposals);
        using var rented = cache.Rent("second", () => throw new InvalidOperationException("Prepared allocation was not reused."));
        Assert.Same(second, rented);
        Assert.Equal(1, cache.IdleCount);
        Assert.Equal(1, second.Clears);
    }

    [Fact]
    public void Oversized_or_uncleared_allocations_are_disposed_and_never_published()
    {
        using var cache = CreateCache(2, 64, allocation => allocation.Clear());
        var tooLarge = new Allocation(65);
        Assert.False(cache.Return("large", tooLarge));
        Assert.Equal(1, tooLarge.Disposals);
        var failedClear = new Allocation(1) { FailClear = true };
        Assert.False(cache.Return("uncleared", failedClear));
        Assert.Equal(1, failedClear.Disposals);
        Assert.Equal(0, cache.IdleCount);
        Assert.Equal(0, cache.IdleBytes);
    }

    [Fact]
    public void Byte_budget_evicts_oldest_idle_allocations_and_disposal_blocks_late_returns()
    {
        var cache = CreateCache(2, 64, allocation => allocation.Clear());
        Allocation first = new(40), second = new(40), late = new(1);
        Assert.True(cache.Return("first", first));
        Assert.True(cache.Return("second", second));
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, cache.IdleCount);
        Assert.Equal(40, cache.IdleBytes);
        cache.Dispose();
        Assert.Equal(1, second.Disposals);
        Assert.False(cache.Return("late", late));
        Assert.Equal(1, late.Disposals);
    }

    [Fact]
    public void Generated_GPU_pixels_are_cleared_before_allocation_reuse()
    {
        var scene = new GlassScene(32, 32, 1, [GlassRegion.Circle(1, 16, 16, 14)]);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Enumerable.Repeat((byte)187, scene.CaptureWidth * scene.CaptureHeight * 4).ToArray();
        byte[] destination = new byte[scene.PixelWidth * scene.PixelHeight * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), destination);
        Assert.Contains(destination, channel => channel != 0);
        renderer.ClearCapturedContent();
        Assert.All(renderer.ReadClearedContentForTesting(), channel => Assert.Equal(0, channel));
        Assert.Throws<InvalidOperationException>(() => renderer.RenderCached(new(), destination));
    }

    [Fact]
    public async Task Prewarming_prepares_reusable_GPU_resources_without_a_capture_session()
    {
        var runtime = new StudioGlassRuntime();
        var scene = new GlassScene(32, 32, 1, [GlassRegion.Circle(1, 16, 16, 14)]);
        try
        {
            await runtime.PrewarmAsync(scene);
            Assert.Equal(1, runtime.IdleCount);
            Assert.InRange(runtime.IdleBytes, 1, 64 * 1024 * 1024);
            nint device;
            using (var lease = runtime.Rent(scene))
            {
                Assert.True(lease.Prepared);
                Assert.NotEqual(0, lease.Device);
                device = lease.Device;
            }
            using (var lease = runtime.Rent(scene)) Assert.Equal(device, lease.Device);
        }
        finally { await runtime.ShutdownAsync(); }
        Assert.Equal(0, runtime.IdleCount);
        Assert.Throws<ObjectDisposedException>(() => runtime.Rent(scene));
    }

    [Fact]
    public async Task Common_warmup_prioritizes_the_admitted_wheel_when_oversized_outputs_exceed_the_idle_budget()
    {
        var runtime = new StudioGlassRuntime();
        const double scale = 1.7; // Deliberately oversized cache stress case, independent of the current display scale.
        const long budget = 64 * 1024 * 1024;
        try
        {
            await runtime.PrewarmWheelAsync(scale);
            Assert.Equal(1, runtime.IdleCount);
            Assert.InRange(runtime.IdleBytes, 1, budget);
            using var primary = runtime.Rent(GlassSceneCache.Shared.GetWheel(0, scale, true, true));
            Assert.True(primary.Prepared, "Resident warmup must retain the actual wheel rather than the demonstration.");
            using var demonstration = runtime.Rent(GlassSceneCache.Shared.GetWheel(0, scale, false, false));
            Assert.False(demonstration.Prepared);
            Assert.InRange(primary.Renderer.AllocationBytes, 1, budget);
            Assert.InRange(demonstration.Renderer.AllocationBytes, 1, budget);
            Assert.True(primary.Renderer.AllocationBytes + demonstration.Renderer.AllocationBytes > budget);
        }
        finally { await runtime.ShutdownAsync(); }
    }

    [Fact]
    public void An_optical_state_change_reuses_the_processed_source_and_matches_a_fresh_render()
    {
        var scene = new GlassScene(32, 32, 1, [GlassRegion.Circle(1, 16, 16, 14)]);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Enumerable.Repeat((byte)137, scene.CaptureWidth * scene.CaptureHeight * 4).ToArray();
        byte[] cached = new byte[scene.PixelWidth * scene.PixelHeight * 4], fresh = new byte[cached.Length];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), fresh);
        var next = new GlassFrameState(AccentRegion: 1, Illumination: .5);
        renderer.RenderCached(next, cached);
        Assert.Equal(1, renderer.SourcePassCount);
        Assert.Equal(2, renderer.OpticalPassCount);
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, next, fresh);
        Assert.Equal(fresh, cached);
    }

    private static StudioGlassAllocationCache<Allocation> CreateCache(int maximumEntries, long maximumBytes, Action<Allocation> clear)
        => new(maximumEntries, maximumBytes, allocation => allocation.Bytes, clear);

    public sealed class Allocation(long bytes) : IDisposable
    {
        public long Bytes { get; } = bytes;
        public int Clears, Disposals;
        public bool FailClear;
        public void Clear() { if (FailClear) throw new InvalidOperationException("Device lost while clearing."); Clears++; }
        public void Dispose() => Disposals++;
    }
}
