using System.Reflection;
using System.Runtime.InteropServices;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class BootstrapScratchResourcesTests
{
    [Fact]
    public void Prepared_scratch_is_empty_reusable_and_cleared_before_disposal()
    {
        Type type = ScratchType();
        using var scratch = Assert.IsAssignableFrom<IDisposable>(Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [8, 6], null));
        Assert.Equal(8 * 6 * 4, Read<long>(type, scratch, "AllocationBytes"));
        nint bits = Read<nint>(type, scratch, "Bits");
        Assert.NotEqual(0, bits);
        Assert.All(Copy(type, scratch), channel => Assert.Equal(0, channel));
        byte[] generated = Enumerable.Repeat((byte)157, 8 * 6 * 4).ToArray();
        Marshal.Copy(generated, 0, bits, generated.Length);
        Assert.Equal(generated, Copy(type, scratch));
        type.GetMethod("Clear", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scratch, null);
        Assert.All(Copy(type, scratch), channel => Assert.Equal(0, channel));
        Assert.Equal(bits, Read<nint>(type, scratch, "Bits"));
    }

    [Fact]
    public async Task Runtime_idle_budget_includes_the_prepared_DIB_with_the_renderer()
    {
        var runtime = new StudioGlassRuntime();
        var scene = new GlassScene(32, 32, 1, [GlassRegion.Circle(1, 16, 16, 14)]);
        try
        {
            long expected;
            using (var lease = runtime.Rent(scene))
            {
                var scratchProperty = lease.GetType().GetProperty("Scratch", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(scratchProperty);
                object scratch = scratchProperty.GetValue(lease)!;
                Assert.NotNull(scratch);
                long scratchBytes = (long)scratch.GetType().GetProperty("AllocationBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scratch)!;
                Assert.Equal(scene.CaptureWidth * scene.CaptureHeight * 4L, scratchBytes);
                expected = lease.Renderer.AllocationBytes + scratchBytes;
            }
            Assert.Equal(1, runtime.IdleCount);
            Assert.Equal(expected, runtime.IdleBytes);
            using var again = runtime.Rent(scene);
            Assert.True(again.Prepared);
        }
        finally { await runtime.ShutdownAsync(); }
    }

    [Fact]
    public async Task Large_wheel_retains_warmed_GPU_resources_when_its_DIB_would_exceed_the_combined_budget()
    {
        var runtime = new StudioGlassRuntime();
        var scene = GlassSceneCache.Shared.GetWheel(0, 1.7, true, true);
        try
        {
            await runtime.PrewarmAsync(scene);
            Assert.InRange(runtime.IdleBytes, 1, 64 * 1024 * 1024);
            Assert.Equal(1, runtime.IdleCount);
            using var lease = runtime.Rent(scene);
            Assert.True(lease.Prepared);
            var property = lease.GetType().GetProperty("Scratch", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(property);
            var scratch = property.GetValue(lease)!;
            Assert.NotNull(scratch);
            Assert.All(Copy(scratch.GetType(), scratch), channel => Assert.Equal(0, channel));
        }
        finally { await runtime.ShutdownAsync(); }
    }

    [Fact]
    public void Scratch_rejects_unbounded_or_invalid_allocation_sizes()
    {
        Type type = ScratchType();
        foreach (var dimensions in new[] { (0, 1), (-1, 4), (8193, 1), (8192, 8192) })
        {
            var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type,
                BindingFlags.Instance | BindingFlags.NonPublic, null, [dimensions.Item1, dimensions.Item2], null));
            Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
        }
    }

    [Fact]
    public void Prepared_capture_places_monitor_tiles_directly_then_copies_once_and_erases_native_storage()
    {
        Type type = ScratchType();
        using var scratch = Assert.IsAssignableFrom<IDisposable>(Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [6, 2], null));
        nint bits = Read<nint>(type, scratch, "Bits");
        GlassMonitor[] monitors = [new(1, -4, 0, 4, 4), new(2, 0, 0, 4, 4)];
        var seen = new List<GlassCaptureTile>();
        bool Write(GlassCaptureTile tile)
        {
            seen.Add(tile);
            byte[] row = new byte[tile.Width * 4];
            for (int y = 0; y < tile.Height; y++)
            {
                for (int x = 0; x < tile.Width; x++)
                {
                    row[x * 4] = (byte)(tile.Monitor.Left + tile.SourceLeft + x + 128);
                    row[x * 4 + 1] = (byte)(tile.Monitor.Top + tile.SourceTop + y);
                    row[x * 4 + 2] = 71;
                }
                Marshal.Copy(row, 0, bits + ((tile.DestinationTop + y) * 6 + tile.DestinationLeft) * 4, row.Length);
            }
            return true;
        }
        byte[]? pixels = CapturePrepared(-3, 1, scratch, monitors, Write);
        Assert.NotNull(pixels);
        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { 0, 3 }, seen.Select(tile => tile.DestinationLeft));
        Assert.Equal(1, Read<int>(type, scratch, "ManagedCopyCount"));
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 6; x++)
        {
            int p = (y * 6 + x) * 4;
            Assert.Equal(x - 3 + 128, pixels[p]); Assert.Equal(y + 1, pixels[p + 1]);
            Assert.Equal(71, pixels[p + 2]); Assert.Equal(255, pixels[p + 3]);
        }
        Assert.All(Copy(type, scratch), channel => Assert.Equal(0, channel));
    }

    [Fact]
    public void Failed_prepared_tile_clears_partial_native_pixels_without_a_managed_copy()
    {
        Type type = ScratchType();
        using var scratch = Assert.IsAssignableFrom<IDisposable>(Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [2, 2], null));
        nint bits = Read<nint>(type, scratch, "Bits");
        Assert.Null(CapturePrepared(0, 0, scratch, [new(1, 0, 0, 2, 2)], _ =>
        { Marshal.Copy(Enumerable.Repeat((byte)189, 16).ToArray(), 0, bits, 16); return false; }));
        Assert.Equal(0, Read<int>(type, scratch, "ManagedCopyCount"));
        Assert.All(Copy(type, scratch), channel => Assert.Equal(0, channel));
    }

    private static Type ScratchType()
    {
        var type = typeof(GlassScene).Assembly.GetType("Citrus.Windows.Presentation.BootstrapCaptureScratch");
        Assert.NotNull(type); return type;
    }
    private static T Read<T>(Type type, object scratch, string name) =>
        (T)type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scratch)!;
    private static byte[] Copy(Type type, object scratch) =>
        (byte[])type.GetMethod("CopyPixels", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scratch, null)!;
    private static byte[]? CapturePrepared(int left, int top, object scratch, IReadOnlyList<GlassMonitor> monitors,
        Func<GlassCaptureTile, bool> copyTile)
    {
        var method = typeof(GlassScene).Assembly.GetType("Citrus.Windows.Presentation.DesktopBootstrapCapture")!
            .GetMethod("CapturePreparedTiles", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (byte[]?)method.Invoke(null, [left, top, scratch, monitors, copyTile, CancellationToken.None]);
    }
}
