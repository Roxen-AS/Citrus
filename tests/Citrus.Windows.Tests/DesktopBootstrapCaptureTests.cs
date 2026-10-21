using System.Reflection;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class DesktopBootstrapCaptureTests
{
    [Fact]
    public void Snapshot_source_exposes_the_padded_admission_contract()
    {
        var type = CaptureType();
        var method = type.GetMethod("TryCapture", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(typeof(byte[]), method.ReturnType);
        Assert.Equal(new[] { typeof(GlassScene), typeof(int), typeof(int), typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void Generated_tiles_preserve_negative_physical_coordinates_and_clear_temporary_source_pixels()
    {
        GlassMonitor[] monitors = [new(1, -100, 0, 100, 100), new(2, 0, 0, 100, 100)];
        var reads = new List<(int X, int Y, int Width, int Height)>();
        var temporary = new List<byte[]>();
        byte[] Read(int x, int y, int width, int height)
        {
            reads.Add((x, y, width, height));
            byte[] pixels = Pattern(x, y, width, height);
            temporary.Add(pixels);
            return pixels;
        }
        var result = Capture(-2, 10, 4, 2, monitors, Read);
        Assert.NotNull(result);
        Assert.Equal(new[] { (-2, 10, 2, 2), (0, 10, 2, 2) }, reads);
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 4; x++) AssertPixel(result, 4, x, y, (byte)(x - 2 + 128), (byte)(y + 10));
        Assert.All(temporary, pixels => Assert.All(pixels, channel => Assert.Equal(0, channel)));
    }

    [Fact]
    public void Padding_beyond_monitor_edges_extends_the_nearest_valid_pixel_instead_of_black()
    {
        GlassMonitor[] monitors = [new(1, 0, 0, 2, 2)];
        var result = Capture(-2, -1, 6, 4, monitors, Pattern);
        Assert.NotNull(result);
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 6; x++)
            AssertPixel(result, 6, x, y, (byte)(Math.Clamp(x - 2, 0, 1) + 128), (byte)Math.Clamp(y - 1, 0, 1));
    }

    [Fact]
    public void Desktop_gaps_extend_the_closest_monitor_without_rescaling_neighboring_tiles()
    {
        GlassMonitor[] monitors = [new(1, 0, 0, 2, 2), new(2, 4, 0, 2, 2)];
        var result = Capture(0, 0, 6, 2, monitors, Pattern);
        Assert.NotNull(result);
        int[] expectedX = [0, 1, 1, 4, 4, 5];
        for (int x = 0; x < expectedX.Length; x++) AssertPixel(result, 6, x, 1, (byte)(expectedX[x] + 128), 1);
    }

    [Fact]
    public void Cancellation_never_reads_or_returns_a_partly_captured_desktop()
    {
        GlassMonitor[] monitors = [new(1, 0, 0, 10, 10)];
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Null(Capture(0, 0, 2, 2, monitors, (_, _, _, _) => throw new InvalidOperationException("Capture must not run."), cancelled.Token));
        using var duringCapture = new CancellationTokenSource();
        byte[] temporary = [];
        Assert.Null(Capture(0, 0, 2, 2, monitors, (x, y, width, height) =>
        {
            temporary = Pattern(x, y, width, height);
            duringCapture.Cancel();
            return temporary;
        }, duringCapture.Token));
        Assert.All(temporary, channel => Assert.Equal(0, channel));
    }

    [Fact]
    public void Unavailable_or_malformed_native_tiles_and_unsafe_bounds_fail_without_fabricated_pixels()
    {
        GlassMonitor[] monitors = [new(1, 0, 0, 10, 10)];
        Assert.Null(Capture(0, 0, 2, 2, monitors, (_, _, _, _) => null));
        Assert.Null(Capture(0, 0, 2, 2, monitors, (_, _, _, _) => [1, 2, 3]));
        Assert.Null(Capture(20, 20, 2, 2, monitors, (_, _, _, _) => throw new InvalidOperationException("No monitor intersects.")));
        Assert.Null(Capture(int.MaxValue - 1, 0, 8, 2, monitors, (_, _, _, _) => throw new InvalidOperationException("Unsafe rectangle.")));
        Assert.Null(Capture(0, 0, 8192, 8192, monitors, (_, _, _, _) => throw new InvalidOperationException("Allocation must remain bounded.")));
    }

    private static Type CaptureType()
    {
        var type = typeof(GlassScene).Assembly.GetType("Citrus.Windows.Presentation.DesktopBootstrapCapture");
        Assert.NotNull(type);
        return type;
    }

    private static byte[]? Capture(int x, int y, int width, int height, IReadOnlyList<GlassMonitor> monitors,
        Func<int, int, int, int, byte[]?> read, CancellationToken token = default)
    {
        var method = CaptureType().GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (byte[]?)method.Invoke(null, [x, y, width, height, monitors, read, token]);
    }

    private static byte[] Pattern(int x, int y, int width, int height)
    {
        var result = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        for (int column = 0; column < width; column++)
        {
            int p = (row * width + column) * 4;
            result[p] = (byte)(x + column + 128); result[p + 1] = (byte)(y + row); result[p + 2] = 23;
            result[p + 3] = 0; // A GDI DIB does not promise alpha; the capture boundary must normalize it.
        }
        return result;
    }

    private static void AssertPixel(byte[] result, int width, int x, int y, byte blue, byte green)
    {
        int p = (y * width + x) * 4;
        Assert.Equal(blue, result[p]); Assert.Equal(green, result[p + 1]);
        Assert.Equal(23, result[p + 2]); Assert.Equal(255, result[p + 3]);
    }
}
