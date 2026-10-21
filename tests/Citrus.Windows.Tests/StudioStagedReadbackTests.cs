using System.Diagnostics;
using System.Reflection;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class StudioStagedReadbackTests
{
    [Fact]
    public async Task Session_retirement_is_bounded_when_completion_never_arrives()
    {
        var scene = Scene();
        var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        Hold(renderer, true); Assert.True(Submit(renderer, scene, Source(scene), 71));
        var constructor = typeof(StudioGlassSession).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        Type captureType = typeof(StudioGlassSession).GetNestedType("MonitorLease", BindingFlags.NonPublic)!;
        var session = (StudioGlassSession)constructor.Invoke([scene, 0, 0, Array.CreateInstance(captureType, 0),
            (Func<IReadOnlyList<GlassMonitor>>)(() => []), (Func<int, int, DesktopGlassCapturePool.Lease?>)((_, _) => null)]);
        typeof(StudioGlassSession).GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, renderer);
        Task retiring = session.RetireAsync();
        try
        {
            Assert.Same(retiring, await Task.WhenAny(retiring, Task.Delay(3000)));
            await retiring;
            Assert.Contains("timeout", session.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Hold(renderer, false);
            await retiring.WaitAsync(TimeSpan.FromSeconds(5));
            session.Dispose();
        }
    }

    [Fact]
    public void Two_busy_slots_reject_more_work_without_changing_source_or_optical_counts()
    {
        var scene = Scene();
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        Hold(renderer, true);
        byte[] source = Source(scene), output = Enumerable.Repeat((byte)37, scene.PixelWidth * scene.PixelHeight * 4).ToArray();
        Assert.True(Submit(renderer, scene, source, 11));
        Assert.True(Submit(renderer, scene, source, 22));
        Assert.Equal(2, Pending(renderer)); Assert.Equal(0, Free(renderer));
        int sourcePasses = renderer.SourcePassCount, opticalPasses = renderer.OpticalPassCount;
        Assert.False(Submit(renderer, scene, source, 33));
        Assert.Equal(sourcePasses, renderer.SourcePassCount); Assert.Equal(opticalPasses, renderer.OpticalPassCount);
        Assert.False(Complete(renderer, output, out _));
        Assert.All(output, channel => Assert.Equal(37, channel));
        Hold(renderer, false);
        Drain(renderer);
    }

    [Fact]
    public void Completion_returns_newest_ready_stamp_and_discards_older_ready_output()
    {
        var scene = Scene();
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        Hold(renderer, true);
        Assert.True(Submit(renderer, scene, Source(scene), 41));
        Assert.True(Submit(renderer, scene, Source(scene), 42));
        Until(() => QueriesCompleted(renderer));
        Hold(renderer, false);
        byte[] output = new byte[scene.PixelWidth * scene.PixelHeight * 4];
        Assert.True(Complete(renderer, output, out long stamp)); Assert.Equal(42, stamp);
        Assert.Equal(0, Pending(renderer)); Assert.Equal(2, Free(renderer));
        Assert.Contains(output, channel => channel != 0);
        Assert.False(Complete(renderer, output, out _));
    }

    [Fact]
    public void Invalidation_drains_pending_GPU_work_without_publishing_canceled_pixels()
    {
        var scene = Scene();
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        Hold(renderer, true); Assert.True(Submit(renderer, scene, Source(scene), 9));
        Method("InvalidateReadbacks").Invoke(renderer, null);
        Until(() => QueriesCompleted(renderer)); Hold(renderer, false);
        byte[] output = Enumerable.Repeat((byte)73, scene.PixelWidth * scene.PixelHeight * 4).ToArray();
        Assert.False(Complete(renderer, output, out long stamp)); Assert.Equal(0, stamp);
        Assert.All(output, channel => Assert.Equal(73, channel)); Assert.Equal(0, Pending(renderer));
    }

    [Theory]
    [InlineData(1d)] [InlineData(1.5d)] [InlineData(2d)]
    public void Staged_output_matches_the_existing_optical_pipeline_at_each_DPI(double scale)
    {
        var scene = Scene(scale);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        using var reference = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Source(scene), actual = new byte[scene.PixelWidth * scene.PixelHeight * 4], expected = new byte[actual.Length];
        reference.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), expected);
        Assert.True(Submit(renderer, scene, source, 101));
        Until(() => Complete(renderer, actual, out _));
        Assert.Equal(expected, actual); Assert.Equal(0, Pending(renderer));
    }

    [Fact]
    public void Cached_submission_does_not_repeat_source_or_Gaussian_passes()
    {
        var scene = Scene();
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] output = new byte[scene.PixelWidth * scene.PixelHeight * 4];
        renderer.RenderPixels(Source(scene), scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), output);
        int sourcePasses = renderer.SourcePassCount, opticalPasses = renderer.OpticalPassCount;
        Assert.True(Assert.IsType<bool>(Method("TrySubmitCached").Invoke(renderer, [new GlassFrameState(), 91L])));
        Until(() => Complete(renderer, output, out _));
        Assert.Equal(sourcePasses, renderer.SourcePassCount); Assert.Equal(opticalPasses + 1, renderer.OpticalPassCount);
    }

    [Fact]
    public void Clear_requires_drain_then_erases_both_staging_slots_before_reuse()
    {
        var scene = Scene();
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        Hold(renderer, true); Assert.True(Submit(renderer, scene, Source(scene), 17));
        Assert.Throws<InvalidOperationException>(renderer.ClearCapturedContent);
        Hold(renderer, false); Drain(renderer); renderer.ClearCapturedContent();
        Assert.All(renderer.ReadClearedContentForTesting(), channel => Assert.Equal(0, channel));
        Assert.Equal(0, Pending(renderer)); Assert.Equal(2, Free(renderer));
        Assert.Throws<TargetInvocationException>(() => Method("TrySubmitCached").Invoke(renderer, [new GlassFrameState(), 18L]));
    }

    [Fact]
    public void Allocation_accounting_includes_two_final_staging_textures_and_query_payloads()
    {
        var scene = Scene();
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        long textureBytes = (long)scene.CaptureWidth * scene.CaptureHeight * 28 + (long)scene.PixelWidth * scene.PixelHeight * 28;
        Assert.True(renderer.AllocationBytes >= textureBytes + 2 * sizeof(int));
        Assert.Equal(2, Free(renderer));
    }

    private static GlassScene Scene(double scale = 1) => new(48, 32, scale,
        [GlassRegion.RoundedRect(1, 2, 2, 44, 28, 6)], GlassMaterial.StudioCalibration with { ShadowStrength = 0 });
    private static byte[] Source(GlassScene scene)
    {
        byte[] pixels = new byte[scene.CaptureWidth * scene.CaptureHeight * 4];
        for (int y = 0; y < scene.CaptureHeight; y++)
        for (int x = 0; x < scene.CaptureWidth; x++)
        {
            int p = (y * scene.CaptureWidth + x) * 4;
            pixels[p] = (byte)((x * 7 + y * 3) % 256); pixels[p + 1] = (byte)((x + y) % 2 == 0 ? 230 : 15);
            pixels[p + 2] = (byte)((x * 3 + y * 11) % 256); pixels[p + 3] = 255;
        }
        return pixels;
    }
    private static bool Submit(D3D11StudioRenderer renderer, GlassScene scene, byte[] source, long stamp) =>
        Assert.IsType<bool>(Method("TrySubmitPixels").Invoke(renderer,
            [source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new GlassFrameState(), stamp]));
    private static bool Complete(D3D11StudioRenderer renderer, byte[] output, out long stamp)
    {
        object?[] args = [output, 0L]; bool completed = Assert.IsType<bool>(Method("TryComplete").Invoke(renderer, args));
        stamp = Assert.IsType<long>(args[1]); return completed;
    }
    private static void Hold(D3D11StudioRenderer renderer, bool hold) => Method("SetReadbackCompletionHeldForTesting").Invoke(renderer, [hold]);
    private static bool QueriesCompleted(D3D11StudioRenderer renderer) => Assert.IsType<bool>(Method("ReadbackQueriesCompletedForTesting").Invoke(renderer, null));
    private static void Drain(D3D11StudioRenderer renderer)
    {
        Method("InvalidateReadbacks").Invoke(renderer, null);
        Until(() => Assert.IsType<bool>(Method("TryDiscardCompletedReadbacks").Invoke(renderer, null)));
    }
    private static int Pending(D3D11StudioRenderer renderer) => Property(renderer, "PendingReadbackCount");
    private static int Free(D3D11StudioRenderer renderer) => Property(renderer, "FreeReadbackSlots");
    private static int Property(D3D11StudioRenderer renderer, string name)
    {
        var property = typeof(D3D11StudioRenderer).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property); return Assert.IsType<int>(property.GetValue(renderer));
    }
    private static MethodInfo Method(string name)
    {
        var method = typeof(D3D11StudioRenderer).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method); return method;
    }
    private static void Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        bool ready;
        while (!(ready = condition()) && watch.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(1);
        Assert.True(ready);
    }
}
