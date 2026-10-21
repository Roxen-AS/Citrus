using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class StudioGlassFrameInvalidationTests
{
    [Fact]
    public void Identical_desktop_and_state_needs_no_optical_or_source_work()
    {
        var stamp = new StudioGlassFrameInvalidation();
        Assert.True(stamp.NeedsRender([4, 8], new(), 1, 2));
        stamp.Commit([4, 8], new(), 1, 2);
        Assert.False(stamp.NeedsSource([4, 8], 1, 2));
        Assert.False(stamp.NeedsRender([4, 8], new(), 1, 2));
    }

    [Fact]
    public void A_fresh_monitor_placement_or_exclusion_requires_processing_fresh_source()
    {
        var stamp = new StudioGlassFrameInvalidation();
        stamp.Commit([4, 8], new(), 1, 2);
        Assert.True(stamp.NeedsSource([5, 8], 1, 2));
        Assert.True(stamp.NeedsSource([4, 8], 2, 2));
        Assert.True(stamp.NeedsSource([4, 8], 1, 3));
        Assert.True(stamp.NeedsSource([-1, 8], 1, 2));
        Assert.True(stamp.NeedsSource([4], 1, 2));
    }

    [Fact]
    public void Pagination_visibility_changes_only_the_optical_pass_and_failed_frames_do_not_advance_the_stamp()
    {
        var stamp = new StudioGlassFrameInvalidation();
        stamp.Commit([4], new(), 1, 2);
        var hiddenPagination = new GlassFrameState(NavigationVisible: false);
        Assert.True(stamp.NeedsRender([4], hiddenPagination, 1, 2));
        Assert.False(stamp.NeedsSource([4], 1, 2));
        Assert.True(stamp.NeedsRender([4], hiddenPagination, 1, 2));
        stamp.Commit([4], hiddenPagination, 1, 2);
        Assert.False(stamp.NeedsRender([4], hiddenPagination, 1, 2));
    }

    [Fact]
    public void Capture_lease_pairs_the_source_version_with_the_delivered_texture_without_breaking_legacy_callbacks()
    {
        var source = new VersionedSource();
        var registry = new DesktopGlassCapturePool.Registry((_, _) => 1, (_, _, _) => source);
        using var lease = Assert.IsType<DesktopGlassCapturePool.Lease>(registry.TryAcquire(0, 0));
        long observed = 0;
        Assert.True(lease.TryUseLatestFrame((_, _, _, _, _, _, _, version) => observed = version));
        Assert.Equal(1, observed);
        source.Version = 2;
        Assert.True(lease.TryUseLatestFrame((_, _, _, _, _, _, _, version) => observed = version));
        Assert.Equal(2, observed);
        int calls = 0;
        Assert.True(lease.TryUseLatestFrame((_, _, _, _, _, _, _) => calls++));
        Assert.Equal(1, calls);
    }

    private sealed class VersionedSource : DesktopGlassCapturePool.ISource
    {
        internal long Version = 1;
        public long FrameVersion => Version;
        public bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int> useFrame)
        { useFrame(1, 2, 3, 100, 100, 0, 0); return true; }
        public void Dispose() { }
    }
}
