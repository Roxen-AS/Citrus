using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class FrostedSceneCacheTests
{
    [Fact]
    public void Cache_reuses_exact_layout_keys_and_evicts_least_recent_fields_within_budget()
    {
        int created = 0;
        var cache = new GlassSceneCache(3200, (_, _, _, _) => { created++; return new(10, 10, 1, []); });
        var first = cache.GetWheel(7, 1, false, true);
        Assert.Same(first, cache.GetWheel(7, 1, false, true));
        var square = cache.GetWheel(7, 1, false, false);
        Assert.NotSame(first, square);
        cache.GetWheel(7, 1, false, true); // Keep first newer than square.
        cache.GetWheel(7, 2, true, true);
        Assert.InRange(cache.RetainedBytes, 0, 3200);
        Assert.Same(first, cache.GetWheel(7, 1, false, true));
        Assert.NotSame(square, cache.GetWheel(7, 1, false, false));
        Assert.Equal(4, created);
    }

    [Fact]
    public void Failed_construction_is_not_cached_and_can_be_retried()
    {
        int attempts = 0;
        var cache = new GlassSceneCache(factory: (_, _, _, _) =>
            ++attempts == 1 ? throw new InvalidOperationException("Owned field failure") : new GlassScene(10, 10, 1, []));
        Assert.Throws<InvalidOperationException>(() => cache.GetWheel(7, 1, false, true));
        Assert.NotNull(cache.GetWheel(7, 1, false, true));
        Assert.Equal(2, attempts);
    }
}
