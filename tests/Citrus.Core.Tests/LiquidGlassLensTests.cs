using System.Diagnostics;
using Citrus.Core.Presentation;
using Xunit.Abstractions;

namespace Citrus.Core.Tests;

public class LiquidGlassLensTests(ITestOutputHelper output)
{
    private const int Size = 368;

    [Fact]
    public void Straight_desktop_stripe_bends_at_the_curved_shell_edge()
    {
        var lens = new LiquidGlassLens(Size, 0);
        var source = Source(Size, (x, _) => x >= 350 ? (byte)255 : (byte)0);
        var (body, _) = Render(lens, source);

        // Same source stripe and screen X. Curvature increases toward the upper-right rim.
        Assert.True(Channel(body, 354, 184) - Channel(body, 354, 145) > 65,
            "A straight stripe must bend toward the curved rim, rather than merely changing color.");
    }

    [Fact]
    public void Edge_displacement_is_nonlinear_and_flat_glass_preserves_source_position()
    {
        var lens = new LiquidGlassLens(Size, 0);
        var source = Source(Size, (x, _) => (byte)Math.Clamp(Math.Round((x - 330) * 255 / 38), 0, 255));
        var (ramp, _) = Render(lens, source);
        var (black, _) = Render(lens, Source(Size, (_, _) => 0));
        var (white, _) = Render(lens, Source(Size, (_, _) => 255));
        double Coordinate(int x) => 330 + 38d * (Channel(ramp, x, 184) - Channel(black, x, 184)) /
            (Channel(white, x, 184) - Channel(black, x, 184));
        double near = 357.5 - Coordinate(357), middle = 355.5 - Coordinate(355), far = 353.5 - Coordinate(353);

        Assert.InRange(near, 3.5, 7.5);
        Assert.True(near > middle && middle > far && far >= 0);
        Assert.True(near - middle > middle - far + .15, "The rounded edge must have a curved, non-linear thickness profile.");
        Assert.InRange(Math.Abs(338.5 - Coordinate(338)), 0, .45);
    }

    [Fact]
    public void Refraction_follows_the_surface_normal_in_both_axes()
    {
        var lens = new LiquidGlassLens(Size, 0);
        var xSource = Source(Size, (x, _) => (byte)Math.Clamp(Math.Round((x - 280) * 255 / 65), 0, 255));
        var ySource = Source(Size, (_, y) => (byte)Math.Clamp(Math.Round((y - 280) * 255 / 65), 0, 255));
        var (xRamp, _) = Render(lens, xSource);
        var (yRamp, _) = Render(lens, ySource);
        var (black, _) = Render(lens, Source(Size, (_, _) => 0));
        var (white, _) = Render(lens, Source(Size, (_, _) => 255));
        const int x = 306, y = 306;
        double Decode(byte[] ramp) => 280 + 65d * (Channel(ramp, x, y) - Channel(black, x, y)) /
            (Channel(white, x, y) - Channel(black, x, y));
        double displacementX = x + .5 - Decode(xRamp), displacementY = y + .5 - Decode(yRamp);

        Assert.InRange(displacementX, 1, 6);
        Assert.InRange(displacementY, 1, 6);
        Assert.InRange(Math.Abs(displacementX - displacementY), 0, .6);
    }

    [Fact]
    public void Petal_bevel_follows_the_actual_rounded_outline()
    {
        var lens = new LiquidGlassLens(Size, 7);
        var (_, lighting) = Render(lens, Source(Size, (_, _) => 120));
        var corner = WheelGeometry.Petal(0, 7)[5];
        var omittedSharpCorner = WheelGeometry.At(WheelGeometry.OuterRadius, -.5 * 360 / 7 + 2);

        Assert.True(AlphaNear(lighting, corner.X, corner.Y, 2) > 15);
        Assert.Equal(0, AlphaNear(lighting, omittedSharpCorner.X, omittedSharpCorner.Y, 0));
        Assert.Equal(0, AlphaNear(lighting, 184, 64, 0)); // Label interior, far from its outline.
        Assert.Equal(0, AlphaNear(lighting, 184, 184, 0)); // Hub interior.
        Assert.True(AlphaNear(lighting, 243, 184, 1) > 15); // Hub has its own curved rim.
    }

    [Fact]
    public void Rounded_petal_refraction_uses_its_own_normal_instead_of_the_shell_normal()
    {
        var petalLens = new LiquidGlassLens(Size, 7);
        var plainShell = new LiquidGlassLens(Size, 0);
        var xSource = Source(Size, (x, _) => (byte)Math.Clamp(Math.Round((x - 118) * 255 / 24), 0, 255));
        var ySource = Source(Size, (_, y) => (byte)Math.Clamp(Math.Round((y - 30) * 255 / 20), 0, 255));
        var (petalX, _) = Render(petalLens, xSource);
        var (petalY, _) = Render(petalLens, ySource);
        var (shellX, _) = Render(plainShell, xSource);
        var (shellY, _) = Render(plainShell, ySource);
        var (black, _) = Render(petalLens, Source(Size, (_, _) => 0));
        var (white, _) = Render(petalLens, Source(Size, (_, _) => 255));

        // Hand-checked fixture on the first seven-target petal's rounded upper-left
        // shoulder. The flattened outline's inward normal is about (.964,.266),
        // versus the shell's (.37,.93).
        const int x = 124, y = 35;
        double contrast = Channel(white, x, y) - Channel(black, x, y);
        double displacementX = (Channel(petalX, x, y) - Channel(shellX, x, y)) * 24 / contrast;
        double displacementY = (Channel(petalY, x, y) - Channel(shellY, x, y)) * 20 / contrast;
        double alongNormal = displacementX * .96395 + displacementY * .26608;
        double acrossNormal = Math.Abs(displacementX * .26608 - displacementY * .96395);

        Assert.InRange(displacementX, 4, 7);
        Assert.InRange(displacementY, 1, 3.5);
        Assert.True(alongNormal > 4);
        Assert.InRange(acrossNormal / alongNormal, 0, .08);
    }

    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    [InlineData(5)][InlineData(6)][InlineData(7)][InlineData(8)][InlineData(9)]
    public void Output_is_premultiplied_and_stays_inside_the_exact_wheel_circle(int count)
    {
        var lens = new LiquidGlassLens(Size, count);
        var (body, lighting) = Render(lens, Source(Size, (_, _) => 255));
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int p = (y * Size + x) * 4;
            if (Math.Pow(x + .5 - 184, 2) + Math.Pow(y + .5 - 184, 2) >= 176 * 176)
            {
                Assert.Equal(0, body[p + 3]);
                Assert.Equal(0, lighting[p + 3]);
            }
            Assert.InRange(body[p], 0, body[p + 3]);
            Assert.InRange(body[p + 1], 0, body[p + 3]);
            Assert.InRange(body[p + 2], 0, body[p + 3]);
            Assert.InRange(lighting[p], 0, lighting[p + 3]);
            Assert.InRange(lighting[p + 1], 0, lighting[p + 3]);
            Assert.InRange(lighting[p + 2], 0, lighting[p + 3]);
        }
        Assert.Equal(255, body[(184 * Size + 184) * 4 + 3]);
    }

    [Fact]
    public void Moving_the_pointer_changes_only_edge_lighting()
    {
        var lens = new LiquidGlassLens(Size, 8);
        var source = Source(Size, (x, y) => (byte)((int)(x + y) % 256));
        var (bodyA, lightA) = Render(lens, source, 20, 20);
        var (bodyB, lightB) = Render(lens, source, 348, 348);

        Assert.Equal(bodyA, bodyB);
        Assert.False(lightA.SequenceEqual(lightB));
        for (int i = 3; i < bodyA.Length; i += 4) Assert.Equal(bodyA[i], bodyB[i]);
    }

    [Fact]
    public void Every_frame_uses_the_new_desktop_without_modifying_or_accumulating_it()
    {
        var lens = new LiquidGlassLens(Size, 4);
        var sourceA = Source(Size, (x, y) => (byte)((int)(x + 3 * y) % 32));
        var original = sourceA.ToArray();
        var sourceB = Source(Size, (_, _) => 255);
        var body = new byte[sourceA.Length];
        var lighting = new byte[sourceA.Length];
        lens.Render(sourceA, body, lighting, 184, 184, false);
        var first = body.ToArray();
        lens.Render(sourceB, body, lighting, 184, 184, false);
        Assert.True(Channel(body, 184, 184) - Channel(first, 184, 184) > 95);
        lens.Render(sourceA, body, lighting, 184, 184, false);
        Assert.Equal(first, body);
        Assert.Equal(original, sourceA);
    }

    [Fact]
    public void Theme_wash_preserves_a_visible_pattern_and_legible_extremes()
    {
        var lens = new LiquidGlassLens(Size, 0);
        var (lightBlack, _) = Render(lens, Source(Size, (_, _) => 0));
        var (lightWhite, _) = Render(lens, Source(Size, (_, _) => 255));
        var (darkBlack, _) = Render(lens, Source(Size, (_, _) => 0), dark: true);
        var (darkWhite, _) = Render(lens, Source(Size, (_, _) => 255), dark: true);

        Assert.InRange(Channel(lightBlack, 184, 184), 125, 170);
        Assert.InRange(Channel(darkWhite, 184, 184), 75, 115);
        Assert.True(Channel(lightWhite, 184, 184) - Channel(lightBlack, 184, 184) > 95);
        Assert.True(Channel(darkWhite, 184, 184) - Channel(darkBlack, 184, 184) > 75);
    }

    [Fact]
    public void Gdi_source_alpha_is_ignored_and_nonfinite_pointer_uses_the_resting_light()
    {
        var lens = new LiquidGlassLens(Size, 2);
        var source = Source(Size, (_, _) => 120);
        var (bodyA, lightA) = Render(lens, source);
        for (int i = 3; i < source.Length; i += 4) source[i] = 0;
        var (bodyB, lightB) = Render(lens, source, double.NaN, double.PositiveInfinity);
        Assert.Equal(bodyA, bodyB);
        Assert.Equal(lightA, lightB);
    }

    [Theory]
    [InlineData(128, 0)][InlineData(1536, 8)]
    public void Supported_surface_bounds_render_without_out_of_range_sampling(int size, int count)
    {
        var lens = new LiquidGlassLens(size, count);
        var source = Source(size, (x, y) => (byte)((int)(x * 3 + y) % 256));
        var (body, lighting) = Render(lens, source, double.MaxValue, -double.MaxValue);
        int center = ((size / 2) * size + size / 2) * 4;
        Assert.Equal(255, body[center + 3]);
        Assert.Contains(lighting, value => value > 0);
    }

    [Fact]
    public void Cached_renderer_allocates_nothing_per_frame()
    {
        var lens = new LiquidGlassLens(Size, 8);
        var source = Source(Size, (_, _) => 120);
        var body = new byte[source.Length];
        var lighting = new byte[source.Length];
        for (int i = 0; i < 3; i++) lens.Render(source, body, lighting, 184, 184, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) lens.Render(source, body, lighting, 10 + i, 350, i % 2 == 0);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Theory]
    [InlineData(368)][InlineData(736)]
    public void Bounded_cached_renderer_reports_frame_cost_without_allocating(int size)
    {
        // Timings are evidence, not a hardware-dependent pass/fail gate.
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        var lens = new LiquidGlassLens(size, 8);
        double mapMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long mapBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.InRange(mapBytes, 0, (long)size * size * 48 + 1_000_000);
        var source = Source(size, (x, y) => (byte)((int)(x + 3 * y) % 256));
        var original = source.ToArray();
        var body = new byte[source.Length];
        var lighting = new byte[source.Length];
        for (int i = 0; i < 15; i++) lens.Render(source, body, lighting, i, 368 - i, false);
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        started = Stopwatch.GetTimestamp();
        const int frames = 30;
        for (int i = 0; i < frames; i++) lens.Render(source, body, lighting, i * 8, 368 - i * 8, i % 2 == 0);
        double frameMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds / frames;
        long frameBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0, frameBytes);
        Assert.Equal(original, source);
        output.WriteLine($"{size}x{size}, 8 petals: map {mapMilliseconds:F2} ms / {mapBytes:N0} allocated bytes; " +
            $"Render mean {frameMilliseconds:F3} ms over {frames} warmed frames; {frameBytes} frame bytes.");
    }

    [Theory]
    [InlineData(127, 0)][InlineData(1537, 0)][InlineData(368, -1)][InlineData(368, 10)]
    public void Invalid_surface_dimensions_and_petal_counts_are_rejected(int size, int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiquidGlassLens(size, count));

    [Fact]
    public void Short_and_overlapping_buffers_are_rejected_before_writing()
    {
        var lens = new LiquidGlassLens(Size, 0);
        var source = Source(Size, (_, _) => 120);
        var body = new byte[source.Length];
        var lighting = new byte[source.Length];
        Assert.Throws<ArgumentException>(() => lens.Render(source.AsSpan(0, 3), body, lighting, 184, 184, false));
        Assert.Throws<ArgumentException>(() => lens.Render(source, body.AsSpan(0, 3), lighting, 184, 184, false));
        Assert.Throws<ArgumentException>(() => lens.Render(source, body, lighting.AsSpan(0, 3), 184, 184, false));
        Assert.Throws<ArgumentException>(() => lens.Render(source, source, lighting, 184, 184, false));
        Assert.Throws<ArgumentException>(() => lens.Render(source, body, source, 184, 184, false));
        Assert.Throws<ArgumentException>(() => lens.Render(source, body, body, 184, 184, false));
    }

    private static byte[] Source(int size, Func<double, double, byte> value)
    {
        var result = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int p = (y * size + x) * 4;
            result[p] = result[p + 1] = result[p + 2] = value((x + .5) * 368 / size, (y + .5) * 368 / size);
            result[p + 3] = 255;
        }
        return result;
    }

    private static (byte[] Body, byte[] Lighting) Render(LiquidGlassLens lens, byte[] source,
        double pointerX = 184, double pointerY = 184, bool dark = false)
    {
        var body = new byte[source.Length];
        var lighting = new byte[source.Length];
        lens.Render(source, body, lighting, pointerX, pointerY, dark);
        return (body, lighting);
    }

    private static byte Channel(byte[] image, int x, int y) => image[(y * Size + x) * 4];

    private static byte AlphaNear(byte[] image, double x, double y, int radius)
    {
        byte maximum = 0;
        for (int iy = Math.Max(0, (int)y - radius); iy <= Math.Min(Size - 1, (int)y + radius); iy++)
        for (int ix = Math.Max(0, (int)x - radius); ix <= Math.Min(Size - 1, (int)x + radius); ix++)
            maximum = Math.Max(maximum, image[(iy * Size + ix) * 4 + 3]);
        return maximum;
    }
}
