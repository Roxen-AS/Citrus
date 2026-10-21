using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class StudioGlassRelocationTests
{
    [Fact]
    public void Moving_within_a_monitor_keeps_its_capture_and_the_scene_alive()
    {
        var scene = Scene();
        var source = new Source();
        int creations = 0;
        GlassMonitor[] monitors = [new(1, 0, 0, 1000, 1000)];
        var registry = new DesktopGlassCapturePool.Registry((_, _) => 1,
            (_, _, _) => { creations++; return source; });
        using var session = Assert.IsType<StudioGlassSession>(StudioGlassSession.TryCreate(scene,
            scene.PaddingPixels + 20, scene.PaddingPixels + 20, () => monitors, registry.TryAcquire));

        Assert.IsAssignableFrom<IRelocatableGlassSession>(session);
        Assert.True(session.TryMoveTo(scene.PaddingPixels + 40, scene.PaddingPixels + 50));
        Assert.Same(scene, session.Scene);
        Assert.Equal(1, creations);
        Assert.Equal(0, source.Disposals);
        session.Dispose();
        Assert.Equal(1, source.Disposals);
    }

    [Fact]
    public void Crossing_a_monitor_boundary_acquires_the_new_capture_before_releasing_the_old_one()
    {
        var scene = Scene();
        var first = new Source(); var second = new Source();
        int creations = 0;
        GlassMonitor[] monitors = [new(1, 0, 0, 1000, 1000), new(2, 1000, 0, 1000, 1000)];
        var registry = new DesktopGlassCapturePool.Registry((x, _) => x < 1000 ? 1 : 2,
            (monitor, _, _) =>
            {
                creations++;
                Assert.Equal(0, first.Disposals);
                return monitor == 1 ? first : second;
            });
        int top = scene.PaddingPixels + 20;
        using var session = Assert.IsType<StudioGlassSession>(StudioGlassSession.TryCreate(scene,
            scene.PaddingPixels + 20, top, () => monitors, registry.TryAcquire));

        Assert.True(session.TryMoveTo(1000 + scene.PaddingPixels - scene.CaptureWidth / 2, top));
        Assert.Equal(2, creations);
        Assert.Equal(0, first.Disposals);
        Assert.Equal(0, second.Disposals);
        Assert.True(session.TryMoveTo(1000 + scene.PaddingPixels + 20, top));
        Assert.Equal(2, creations);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(0, second.Disposals);
        session.Dispose();
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_relocation_releases_every_new_capture_and_preserves_the_previous_plan(bool throwOnAcquire)
    {
        var scene = Scene();
        var original = new Source(); var partial = new Source();
        GlassMonitor[] monitors = [new(1, 0, 0, 1000, 1000), new(2, 1000, 0, 1000, 1000),
            new(3, 2000, 0, 1000, 1000)];
        int enumerations = 0;
        var registry = new DesktopGlassCapturePool.Registry((x, _) => x < 1000 ? 1 : x < 2000 ? 2 : 3,
            (monitor, _, _) => monitor switch
            {
                1 => original,
                2 => partial,
                _ => throwOnAcquire ? throw new InvalidOperationException("Capture unavailable.") : null
            });
        int left = scene.PaddingPixels + 20, top = scene.PaddingPixels + 20;
        using var session = Assert.IsType<StudioGlassSession>(StudioGlassSession.TryCreate(scene,
            left, top, () => { enumerations++; return monitors; }, registry.TryAcquire));

        Assert.False(session.TryMoveTo(2000 + scene.PaddingPixels - scene.CaptureWidth / 2, top));
        Assert.Equal(0, original.Disposals);
        Assert.Equal(1, partial.Disposals);
        Assert.Equal(2, enumerations);
        Assert.True(session.TryMoveTo(left, top));
        Assert.Equal(2, enumerations);
        session.Dispose();
        Assert.Equal(1, original.Disposals);
        Assert.Equal(1, partial.Disposals);
    }

    [Fact]
    public void An_unavailable_or_unrepresentable_destination_keeps_the_existing_capture()
    {
        var scene = Scene();
        var source = new Source();
        GlassMonitor[] monitors = [new(1, 0, 0, 1000, 1000)];
        var registry = new DesktopGlassCapturePool.Registry((_, _) => 1, (_, _, _) => source);
        using var session = Assert.IsType<StudioGlassSession>(StudioGlassSession.TryCreate(scene,
            scene.PaddingPixels + 20, scene.PaddingPixels + 20, () => monitors, registry.TryAcquire));

        Assert.False(session.TryMoveTo(4000, 4000));
        Assert.False(session.TryMoveTo(int.MinValue, int.MinValue));
        Assert.Equal(0, source.Disposals);
        session.Dispose();
        Assert.Equal(1, source.Disposals);
        Assert.Throws<ObjectDisposedException>(() => session.TryMoveTo(0, 0));
    }

    private static GlassScene Scene() => new(20, 20, 1, [GlassRegion.Circle(1, 10, 10, 8)]);

    private sealed class Source : DesktopGlassCapturePool.ISource
    {
        internal int Disposals;
        public bool TryUseLatestFrame(Action<nint, nint, nint, int, int, int, int> useFrame) => false;
        public void Dispose() => Disposals++;
    }
}
