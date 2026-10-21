using System.Reflection;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class StudioGlassOutstandingResourcesTests
{
    [Fact]
    public async Task Rapid_canceled_held_readbacks_cannot_exceed_four_outstanding_allocations()
    {
        var runtime = new StudioGlassRuntime();
        var scene = Scene();
        var leases = new List<StudioGlassRuntime.Lease>();
        var renderers = new List<D3D11StudioRenderer>();
        try
        {
            Assert.Equal(0, Outstanding(runtime));
            for (int i = 0; i < 4; i++)
            {
                var lease = runtime.Rent(scene);
                leases.Add(lease); renderers.Add(lease.Renderer);
            }
            for (int i = 0; i < leases.Count; i++)
            {
                var lease = leases[i];
                HoldGeneratedReadback(lease.Renderer, true);
                lock (DesktopGlassCapturePool.GpuGate)
                    Assert.True(lease.Renderer.TrySubmitPixels(new byte[scene.CaptureWidth * scene.CaptureHeight * 4],
                        scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), i + 1));
                lease.Dispose();
                Assert.Equal(4, Outstanding(runtime));
            }
            for (int attempt = 0; attempt < 20; attempt++)
                Assert.Throws<InvalidOperationException>(() => runtime.Rent(scene));
            Assert.Equal(4, Outstanding(runtime));
            Assert.InRange(Retiring(runtime), 1, 4);
            Assert.False(runtime.RetirementCompletion.IsCompleted);
            foreach (var renderer in renderers) HoldGeneratedReadback(renderer, false);
            await runtime.RetirementCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, Outstanding(runtime));
            Assert.Equal(0, Retiring(runtime));
            Assert.InRange(runtime.IdleCount, 0, 2);
            Assert.InRange(runtime.IdleBytes, 0, 64 * 1024 * 1024);
            using var next = runtime.Rent(scene);
            Assert.Equal(1, Outstanding(runtime));
        }
        finally
        {
            foreach (var renderer in renderers) HoldGeneratedReadback(renderer, false);
            foreach (var lease in leases) lease.Dispose();
            await runtime.RetirementCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await runtime.ShutdownAsync();
        }
        Assert.Equal(0, Outstanding(runtime));
    }

    [Fact]
    public async Task Reset_does_not_forget_still_rented_old_generation_allocations()
    {
        var runtime = new StudioGlassRuntime();
        var scene = Scene();
        var leases = new List<StudioGlassRuntime.Lease>();
        try
        {
            Assert.Equal(0, Outstanding(runtime));
            for (int i = 0; i < 4; i++) leases.Add(runtime.Rent(scene));
            runtime.ResetForTesting();
            Assert.Equal(4, Outstanding(runtime));
            Assert.Throws<InvalidOperationException>(() => runtime.Rent(scene));
            foreach (var lease in leases) lease.Dispose();
            Assert.Equal(0, Outstanding(runtime));
            Assert.Equal(0, runtime.IdleCount);
            using var next = runtime.Rent(scene);
            Assert.Equal(1, Outstanding(runtime));
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
            await runtime.ShutdownAsync();
        }
        Assert.Equal(0, Outstanding(runtime));
    }

    private static GlassScene Scene() => new(32, 32, 1, [GlassRegion.Circle(1, 16, 16, 14)]);
    private static int Outstanding(StudioGlassRuntime runtime)
    {
        var property = typeof(StudioGlassRuntime).GetProperty("OutstandingAllocationCount", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<int>(property.GetValue(runtime));
    }
    private static int Retiring(StudioGlassRuntime runtime)
    {
        lock (DesktopGlassCapturePool.GpuGate)
            return Assert.IsType<HashSet<Task>>(typeof(StudioGlassRuntime)
                .GetField("retirements", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)).Count;
    }
    private static void HoldGeneratedReadback(D3D11StudioRenderer renderer, bool held)
    {
        lock (DesktopGlassCapturePool.GpuGate)
        {
            // Only generated zero pixels enter these leased renderers. Delay
            // injection is forbidden for actual captured desktop renderers.
            typeof(D3D11StudioRenderer).GetField("generatedTestSession", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, true);
            renderer.SetReadbackCompletionHeldForTesting(held);
        }
    }
}
