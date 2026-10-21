using System.Reflection;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class CaptureCapabilityPrewarmTests
{
    [Fact]
    public void Source_exposes_capture_free_capability_preparation()
    {
        var method = typeof(DesktopGlassSource).GetMethod("PrewarmCapabilities", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    [Fact]
    public async Task Preparation_and_activation_share_one_completed_probe_under_concurrent_use()
    {
        int supportCalls = 0, accessCalls = 0;
        var cache = new CaptureCapabilityCache(() => { Interlocked.Increment(ref supportCalls); return true; },
            () => { Interlocked.Increment(ref accessCalls); return true; });
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(cache.Resolve)));
        Assert.All(results, Assert.True);
        Assert.True(cache.Resolve());
        Assert.Equal(1, supportCalls);
        Assert.Equal(1, accessCalls);
    }

    [Fact]
    public void Unsupported_capture_never_requests_access_and_can_retry_on_activation()
    {
        int calls = 0;
        var cache = new CaptureCapabilityCache(() => { calls++; return false; }, () => throw new InvalidOperationException("Access must not run."));
        Assert.False(cache.Resolve());
        Assert.False(cache.Resolve());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Initially_unsupported_capture_can_recover_then_reuse_the_successful_probe()
    {
        int supportCalls = 0, accessCalls = 0;
        var cache = new CaptureCapabilityCache(() => ++supportCalls > 1, () => { accessCalls++; return true; });
        Assert.False(cache.Resolve());
        Assert.True(cache.Resolve());
        Assert.True(cache.Resolve());
        Assert.Equal(2, supportCalls);
        Assert.Equal(1, accessCalls);
    }

    [Fact]
    public void Unavailable_access_is_retried_without_discarding_a_later_success()
    {
        int supportCalls = 0, accessCalls = 0;
        var cache = new CaptureCapabilityCache(() => { supportCalls++; return true; }, () => ++accessCalls > 1);
        Assert.False(cache.Resolve());
        Assert.True(cache.Resolve());
        Assert.True(cache.Resolve());
        Assert.Equal(2, supportCalls);
        Assert.Equal(2, accessCalls);
    }

    [Fact]
    public void Transient_preparation_failure_does_not_poison_the_activation_retry()
    {
        int calls = 0;
        var cache = new CaptureCapabilityCache(() => ++calls == 1 ? throw new InvalidOperationException("Transient initialization failure.") : true, () => true);
        Assert.Throws<InvalidOperationException>(() => cache.Resolve());
        Assert.True(cache.Resolve());
        Assert.Equal(2, calls);
    }
}
