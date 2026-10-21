using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;

namespace Citrus.Windows.Tests;

public sealed class StudioGpuRendererTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 0d)]
    [InlineData(true, 1d)]
    public void Wheel_rim_highlights_stay_bounded_after_display_gamut_clipping(bool dark, double illumination)
    {
        var wheel = GlassScene.WheelPresentation(7, 1, true);
        var unlit = new GlassScene(wheel.WidthDip, wheel.HeightDip, wheel.Scale, wheel.Regions,
            wheel.Material with { FresnelIntensity = 0, GlareIntensity = 0 }, wheel.LightingReferenceHeightDip,
            wheel.LightingCenterXDip, wheel.LightingCenterYDip);
        using var renderer = D3D11StudioRenderer.CreateForTesting(wheel, software: true);
        using var unlitRenderer = D3D11StudioRenderer.CreateForTesting(unlit, software: true);
        byte[] source = Solid(wheel.CaptureWidth, wheel.CaptureHeight, 72, 106, 132);
        byte[] actual = new byte[wheel.PixelWidth * wheel.PixelHeight * 4], body = new byte[actual.Length];
        var state = new GlassFrameState(Dark: dark, Illumination: illumination);
        renderer.RenderPixels(source, wheel.CaptureWidth, wheel.CaptureHeight, 0, 0, state, actual);
        unlitRenderer.RenderPixels(source, unlit.CaptureWidth, unlit.CaptureHeight, 0, 0, state, body);

        double maximumLiftFraction = 0;
        int sampled = 0, clippedWhite = 0;
        for (int pixel = 0; pixel < actual.Length; pixel += 4)
        {
            // Only the continuous backing and navigation own optical shoulders.
            if (wheel.Field[pixel + 2] < .5 || wheel.Field[pixel + 2] > 6 || actual[pixel + 3] != 255) continue;
            sampled++;
            if (actual[pixel] >= 250 && actual[pixel + 1] >= 250 && actual[pixel + 2] >= 250) clippedWhite++;
            for (int channel = 0; channel < 3; channel++)
                maximumLiftFraction = Math.Max(maximumLiftFraction,
                    (actual[pixel + channel] - body[pixel + channel]) / (255d - body[pixel + channel]));
        }
        output.WriteLine($"Wheel rim: {sampled} opaque shoulder pixels, {clippedWhite} clipped whites, peak highlight lift {maximumLiftFraction:P1}.");
        Assert.True(sampled > 1_000, "The check must cover the full wheel's visible shoulders.");
        Assert.Equal(0, clippedWhite);
        Assert.InRange(maximumLiftFraction, .05, .14);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    public void Wheel_contour_keeps_subpixel_coverage_without_an_exterior_black_halo(double scale)
    {
        var wheel = GlassScene.WheelPresentation(7, scale, true);
        using var renderer = D3D11StudioRenderer.CreateForTesting(wheel, software: true);
        byte[] target = new byte[wheel.PixelWidth * wheel.PixelHeight * 4];
        renderer.RenderPixels(Solid(wheel.CaptureWidth, wheel.CaptureHeight, 175, 185, 195),
            wheel.CaptureWidth, wheel.CaptureHeight, 0, 0, new(), target);

        int exteriorPixels = 0, tintedExteriorPixels = 0, partialCoveragePixels = 0;
        int maximumCoverageError = 0;
        for (int pixel = 0; pixel < target.Length; pixel += 4)
        {
            // One DIP smooth coverage belongs only to the backing silhouette.
            // No separate shadow alpha may extend it.
            double width = Math.Max(1 / scale, wheel.Material.EdgeFeatherDip);
            double t = Math.Clamp(wheel.Field[pixel + 2] / width + .5, 0, 1);
            double coverage = t * t * (3 - 2 * t);
            int expectedAlpha = (int)Math.Round(coverage * 255);
            maximumCoverageError = Math.Max(maximumCoverageError, Math.Abs(target[pixel + 3] - expectedAlpha));
            if (coverage is > 0 and < 1) partialCoveragePixels++;
            if (coverage > 0) continue;
            exteriorPixels++;
            if (target[pixel] != 0 || target[pixel + 1] != 0 || target[pixel + 2] != 0 || target[pixel + 3] != 0)
                tintedExteriorPixels++;
        }

        output.WriteLine($"Wheel {scale:0.0}x contour: {partialCoveragePixels} antialiased pixels, " +
            $"{tintedExteriorPixels}/{exteriorPixels} exterior pixels tinted, maximum alpha error {maximumCoverageError}.");
        Assert.True(exteriorPixels > 1_000, "The check must include the transparent corners and petal gaps.");
        Assert.True(partialCoveragePixels > 100, "The wheel must retain its antialiased silhouette.");
        Assert.Equal(0, tintedExteriorPixels);
        Assert.InRange(maximumCoverageError, 0, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wheel_shoulder_and_partial_coverage_do_not_add_a_dark_contact_line(bool dark)
    {
        var wheel = GlassScene.WheelPresentation(7, 1, true);
        var unshadowed = new GlassScene(wheel.WidthDip, wheel.HeightDip, wheel.Scale, wheel.Regions,
            wheel.Material with { ShadowStrength = 0 }, wheel.LightingReferenceHeightDip,
            wheel.LightingCenterXDip, wheel.LightingCenterYDip);
        using var renderer = D3D11StudioRenderer.CreateForTesting(wheel, software: true);
        using var referenceRenderer = D3D11StudioRenderer.CreateForTesting(unshadowed, software: true);
        byte[] source = Solid(wheel.CaptureWidth, wheel.CaptureHeight, 175, 185, 195);
        byte[] actual = new byte[wheel.PixelWidth * wheel.PixelHeight * 4], reference = new byte[actual.Length];
        var state = new GlassFrameState(Dark: dark, Illumination: 1);
        renderer.RenderPixels(source, wheel.CaptureWidth, wheel.CaptureHeight, 0, 0, state, actual);
        referenceRenderer.RenderPixels(source, unshadowed.CaptureWidth, unshadowed.CaptureHeight, 0, 0, state, reference);

        int partialPixels = 0, shoulderPixels = 0, maximumChannelDifference = 0, maximumAlphaDifference = 0;
        double maximumStraightColorDifference = 0;
        for (int pixel = 0; pixel < actual.Length; pixel += 4)
        {
            if (wheel.Field[pixel + 2] <= -.5 || wheel.Field[pixel + 2] > 6) continue;
            shoulderPixels++;
            maximumAlphaDifference = Math.Max(maximumAlphaDifference, Math.Abs(actual[pixel + 3] - reference[pixel + 3]));
            bool partial = reference[pixel + 3] is > 8 and < 247;
            if (partial) partialPixels++;
            for (int channel = 0; channel < 3; channel++)
            {
                maximumChannelDifference = Math.Max(maximumChannelDifference,
                    Math.Abs(actual[pixel + channel] - reference[pixel + channel]));
                if (partial)
                    maximumStraightColorDifference = Math.Max(maximumStraightColorDifference,
                        Math.Abs(actual[pixel + channel] / (double)actual[pixel + 3] -
                            reference[pixel + channel] / (double)reference[pixel + 3]));
            }
        }

        output.WriteLine($"Wheel dark={dark}: {shoulderPixels} shoulder pixels, {partialPixels} partial pixels; " +
            $"max RGB/alpha deltas {maximumChannelDifference}/{maximumAlphaDifference}, " +
            $"straight-color delta {maximumStraightColorDifference:0.0000}.");
        Assert.True(shoulderPixels > 1_000 && partialPixels > 100, "Exercise the visible shoulders and antialiased contour.");
        Assert.InRange(maximumChannelDifference, 0, 1);
        Assert.InRange(maximumAlphaDifference, 0, 1);
        Assert.InRange(maximumStraightColorDifference, 0, .01);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Wheel_soft_edges_generate_owned_before_and_after_optical_evidence(bool dark, bool densePattern)
    {
        var wheel = GlassScene.WheelPresentation(7, WheelPresentationMetrics.Scale, true);
        // Retain the previous wheel preset as a controlled visual comparison.
        var previous = new GlassScene(wheel.WidthDip, wheel.HeightDip, wheel.Scale, wheel.Regions,
            wheel.Material with { ShadowStrength = .1417, EdgeHighlightOpacity = .55 },
            wheel.LightingReferenceHeightDip, wheel.LightingCenterXDip, wheel.LightingCenterYDip);
        byte[] source = CreateSource();
        byte[] before = Render(previous), after = Render(wheel);
        Assert.False(before.AsSpan().SequenceEqual(after), "The real optical pipeline must visibly change.");
        Assert.Equal(0, after[3]);

        byte[] oldComposite = Composite(before), newComposite = Composite(after);
        string folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "studio-optics");
        Directory.CreateDirectory(folder);
        string name = $"wheel-soft-edges-{(dark ? "dark" : "light")}-{(densePattern ? "dense" : "flat")}";
        SavePng(Path.Combine(folder, name + "-before.png"), oldComposite, wheel.PixelWidth, wheel.PixelHeight);
        SavePng(Path.Combine(folder, name + "-after.png"), newComposite, wheel.PixelWidth, wheel.PixelHeight);
        const int gap = 12;
        int pairWidth = wheel.PixelWidth * 2 + gap;
        byte[] pair = Solid(pairWidth, wheel.PixelHeight, 128, 128, 128);
        for (int y = 0; y < wheel.PixelHeight; y++)
        {
            oldComposite.AsSpan(y * wheel.PixelWidth * 4, wheel.PixelWidth * 4).CopyTo(pair.AsSpan(y * pairWidth * 4));
            newComposite.AsSpan(y * wheel.PixelWidth * 4, wheel.PixelWidth * 4)
                .CopyTo(pair.AsSpan((y * pairWidth + wheel.PixelWidth + gap) * 4));
        }
        string pairPath = Path.Combine(folder, name + "-before-after.png");
        SavePng(pairPath, pair, pairWidth, wheel.PixelHeight);
        output.WriteLine($"Owned synthetic source only; WARP uses the production optical shader at wheel scale {WheelPresentationMetrics.Scale}. " +
            $"Selected petal, old left / softened right: {pairPath}");

        byte[] Render(GlassScene scene)
        {
            using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
            byte[] pixels = new byte[scene.PixelWidth * scene.PixelHeight * 4];
            renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0,
                new(AccentRegion: 3, Dark: dark, Illumination: 1), pixels);
            return pixels;
        }

        byte[] Composite(byte[] pixels)
        {
            byte[] result = new byte[pixels.Length];
            for (int y = 0; y < wheel.PixelHeight; y++)
            for (int x = 0; x < wheel.PixelWidth; x++)
            {
                int pixel = (y * wheel.PixelWidth + x) * 4;
                int background = ((y + wheel.PaddingPixels) * wheel.CaptureWidth + x + wheel.PaddingPixels) * 4;
                int inverseAlpha = 255 - pixels[pixel + 3];
                for (int channel = 0; channel < 3; channel++)
                    result[pixel + channel] = (byte)Math.Min(255,
                        pixels[pixel + channel] + (source[background + channel] * inverseAlpha + 127) / 255);
                result[pixel + 3] = 255;
            }
            return result;
        }

        byte[] CreateSource()
        {
            byte[] pixels = Solid(wheel.CaptureWidth, wheel.CaptureHeight,
                dark ? (byte)39 : (byte)241, dark ? (byte)42 : (byte)244, dark ? (byte)48 : (byte)247);
            if (!densePattern) return pixels;
            (byte Blue, byte Green, byte Red)[] palette = dark
                ? [(43, 57, 70), (70, 48, 40), (47, 64, 45), (61, 45, 64)]
                : [(225, 235, 250), (243, 218, 206), (226, 238, 219), (243, 231, 241)];
            for (int y = 0; y < wheel.CaptureHeight; y++)
            for (int x = 0; x < wheel.CaptureWidth; x++)
            {
                double bodyX = (x - wheel.PaddingPixels + .5) / wheel.Scale;
                double bodyY = (y - wheel.PaddingPixels + .5) / wheel.Scale;
                int tile = (int)Math.Floor(bodyX / 90) + (int)Math.Floor(bodyY / 110);
                var color = palette[(tile % palette.Length + palette.Length) % palette.Length];
                double lineY = bodyY - Math.Floor(bodyY / 22) * 22;
                bool text = lineY is >= 9 and < 11 && ((int)Math.Floor(bodyX / 7) % 10 + 10) % 10 < 7;
                if (text) color = dark ? ((byte)100, (byte)103, (byte)108) : ((byte)156, (byte)160, (byte)165);
                int pixel = (y * wheel.CaptureWidth + x) * 4;
                pixels[pixel] = color.Blue; pixels[pixel + 1] = color.Green; pixels[pixel + 2] = color.Red;
            }
            return pixels;
        }
    }

    [Fact]
    public void Wheel_selection_keeps_a_full_orange_fill_over_a_bright_desktop()
    {
        var material = GlassScene.Wheel(7, 1).Material with { FresnelIntensity = 0, GlareIntensity = 0, ShadowStrength = 0 };
        var scene = new GlassScene(160, 160, 1, [GlassRegion.Circle(1, 80, 80, 70)], material);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] target = new byte[160 * 160 * 4];
        renderer.RenderPixels(Solid(scene.CaptureWidth, scene.CaptureHeight, 255, 255, 255), scene.CaptureWidth,
            scene.CaptureHeight, 0, 0, new(AccentRegion: 1), target);
        int center = (80 * 160 + 80) * 4;
        Assert.InRange(target[center], 0, 20);
        Assert.InRange(target[center + 1], 75, 100);
        Assert.InRange(target[center + 2], 250, 255);
        Assert.Equal(255, target[center + 3]);
    }

    [Fact]
    public void Native_device_and_scene_arguments_are_required() =>
        Assert.Throws<ArgumentNullException>(() => new D3D11StudioRenderer(0, 0, null!));

    [Fact]
    public void Shader_bytecode_can_be_precompiled_without_a_scene_or_capture_device() => D3D11StudioRenderer.PrecompileShaders();

    [Theory]
    [InlineData(false, 0, 0, 0, 173, 173, 173)]
    [InlineData(false, 255, 255, 255, 255, 255, 255)]
    [InlineData(false, 255, 0, 0, 255, 173, 173)]
    [InlineData(true, 0, 0, 0, 14, 12, 11)]
    [InlineData(true, 255, 255, 255, 106, 104, 103)]
    [InlineData(true, 255, 0, 0, 106, 12, 11)]
    public void Neutral_frost_blends_each_channel_after_sampling_and_before_selection(bool dark,
        byte blue, byte green, byte red, int expectedBlue, int expectedGreen, int expectedRed)
    {
        var material = GlassMaterial.Citrus with { GlareIntensity = 0, FresnelIntensity = 0, ShadowStrength = 0 };
        var scene = Circle(material);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] ordinary = new byte[160 * 160 * 4], selected = new byte[ordinary.Length];
        renderer.RenderPixels(Solid(scene.CaptureWidth, scene.CaptureHeight, blue, green, red), scene.CaptureWidth,
            scene.CaptureHeight, 0, 0, new(Dark: dark), ordinary);
        renderer.RenderPixels(Solid(scene.CaptureWidth, scene.CaptureHeight, blue, green, red), scene.CaptureWidth,
            scene.CaptureHeight, 0, 0, new(AccentRegion: 1, Dark: dark), selected);
        int center = (80 * 160 + 80) * 4;
        Assert.InRange(ordinary[center], expectedBlue - 1, expectedBlue + 1);
        Assert.InRange(ordinary[center + 1], expectedGreen - 1, expectedGreen + 1);
        Assert.InRange(ordinary[center + 2], expectedRed - 1, expectedRed + 1);
        // The existing .9 selection accent mixes at .8 intensity after neutral frosting.
        Assert.InRange(selected[center + 2], (int)Math.Round(expectedRed * .28 + 255 * .72) - 2,
            (int)Math.Round(expectedRed * .28 + 255 * .72) + 2);
        Assert.Equal(255, ordinary[center + 3]);
        double luminance = Luminance(ordinary[center + 2], ordinary[center + 1], ordinary[center]);
        double contrast = dark ? 1.05 / (luminance + .05) : (luminance + .05) / .05;
        Assert.True(contrast >= 4.5, $"Adaptive glyph contrast was {contrast:0.00}:1.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Frost_preserves_broad_desktop_texture_and_softens_fine_detail(bool dark)
    {
        var material = GlassMaterial.Citrus with { GlareIntensity = 0, FresnelIntensity = 0, ShadowStrength = 0 };
        var scene = Circle(material);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Solid(scene.CaptureWidth, scene.CaptureHeight, 0, 0, 0);
        for (int y = 0; y < scene.CaptureHeight; y++)
        for (int x = scene.PaddingPixels + 80; x < scene.CaptureWidth; x++)
        {
            int offset = (y * scene.CaptureWidth + x) * 4;
            source[offset] = source[offset + 1] = source[offset + 2] = 255;
        }
        byte[] target = new byte[160 * 160 * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(Dark: dark), target);
        int left = (80 * 160 + 60) * 4, right = (80 * 160 + 100) * 4;
        Assert.True(target[right] - target[left] >= (dark ? 85 : 75), "Broad source texture must survive the neutral tint.");
        int before = (80 * 160 + 79) * 4, after = (80 * 160 + 80) * 4;
        Assert.InRange(target[after] - target[before], 1, 25);
        Assert.Equal(255, target[left + 3]);
        Assert.Equal(0, target[3]);
    }

    [Fact]
    public void Enlarged_presentation_preserves_wheel_lighting_within_one_display_level()
    {
        var square = GlassScene.Wheel(3, 1);
        var full = GlassScene.WheelPresentation(3, 1, true);
        using var squareRenderer = D3D11StudioRenderer.CreateForTesting(square, software: true);
        using var fullRenderer = D3D11StudioRenderer.CreateForTesting(full, software: true);
        byte[] source = Solid(full.CaptureWidth, full.CaptureHeight, 73, 129, 181);
        byte[] expected = new byte[square.PixelWidth * square.PixelHeight * 4];
        byte[] actual = new byte[full.PixelWidth * full.PixelHeight * 4];
        squareRenderer.RenderPixels(source, full.CaptureWidth, full.CaptureHeight, 0, 0, new(Illumination: .8), expected);
        fullRenderer.RenderPixels(source, full.CaptureWidth, full.CaptureHeight, 0, 0, new(Illumination: .8), actual);
        // Different padded texture heights can round RGBA16F Gaussian interpolation
        // on opposite sides of a final8-bit boundary. Geometry and alpha stay exact.
        for (int index = 0; index < expected.Length; index++)
            if (index % 4 == 3) Assert.Equal(expected[index], actual[index]);
            else Assert.InRange(Math.Abs(expected[index] - actual[index]), 0, 1);
        Assert.Equal(255, actual[(390 * 368 + 184) * 4 + 3]);
    }

    [Fact]
    public void Flat_interior_is_clear_and_every_output_pixel_is_contour_clipped_and_premultiplied()
    {
        var scene = Circle(new GlassMaterial { ShadowStrength = 0 });
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Solid(scene.CaptureWidth, scene.CaptureHeight, 73, 129, 181);
        byte[] target = new byte[scene.PixelWidth * scene.PixelHeight * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), target);
        int center = (80 * 160 + 80) * 4;
        Assert.InRange(target[center], 72, 74); Assert.InRange(target[center + 1], 128, 130);
        Assert.InRange(target[center + 2], 180, 182); Assert.Equal(255, target[center + 3]);
        Assert.Equal(0, target[3]);
        for (int p = 0; p < target.Length; p += 4)
        {
            Assert.True(target[p] <= target[p + 3]); Assert.True(target[p + 1] <= target[p + 3]);
            Assert.True(target[p + 2] <= target[p + 3]);
        }
    }

    [Fact]
    public void Broad_optical_shadow_keeps_the_tight_window_exterior_compact()
    {
        var scene = new GlassScene(100, 80, 1, [GlassRegion.RoundedRect(1, 0, 0, 100, 80, 24)], GlassMaterial.Citrus);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] target = new byte[100 * 80 * 4];
        renderer.RenderPixels(Solid(scene.CaptureWidth, scene.CaptureHeight, 80, 120, 160), scene.CaptureWidth,
            scene.CaptureHeight, 0, 0, new(), target);
        foreach (int corner in new[] { 0, 99, 79 * 100, 80 * 100 - 1 }) Assert.Equal(0, target[corner * 4 + 3]);
        Assert.Equal(255, target[(40 * 100 + 50) * 4 + 3]);
    }

    [Fact]
    public void Compact_exterior_shadow_is_black_premultiplied_and_keeps_body_corners_transparent()
    {
        var material = new GlassMaterial { ShadowExpandDip = 2, ShadowStrength = .5, ShadowOffsetYDip = 0 };
        var scene = new GlassScene(100, 100, 1, [GlassRegion.RoundedRect(1, 20, 20, 60, 60, 12)], material);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] target = new byte[100 * 100 * 4];
        renderer.RenderPixels(Solid(scene.CaptureWidth, scene.CaptureHeight, 128, 128, 128), scene.CaptureWidth,
            scene.CaptureHeight, 0, 0, new(), target);
        int outside = (50 * 100 + 18) * 4; // 1.5DIP outside the straight left edge.
        int expectedAlpha = (int)Math.Round(Math.Exp(-1.5 / 2) * .6 * .5 * 255);
        Assert.InRange(target[outside + 3], expectedAlpha - 1, expectedAlpha + 1);
        Assert.Equal(0, target[outside]); Assert.Equal(0, target[outside + 1]); Assert.Equal(0, target[outside + 2]);
        Assert.Equal(0, target[3]); Assert.Equal(0, target[(99 * 100 + 99) * 4 + 3]);
        Assert.Equal(255, target[(50 * 100 + 50) * 4 + 3]);
        int farOutside = (50 * 100 + 2) * 4;
        Assert.Equal(0, target[farOutside + 3]);
    }

    [Fact]
    public void Padded_monitor_tiles_overlay_the_anchor_without_CPU_intermediate_compositing()
    {
        var scene = new GlassScene(160, 160, 1, [GlassRegion.RoundedRect(1, 8, 8, 144, 144, 12)],
            new GlassMaterial { ShadowStrength = 0, BlurRadiusDip = 0, FresnelIntensity = 0, GlareIntensity = 0 });
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Solid(scene.CaptureWidth, scene.CaptureHeight, 20, 50, 180);
        for (int y = 0; y < scene.CaptureHeight; y++)
        for (int x = scene.CaptureWidth / 2; x < scene.CaptureWidth; x++)
        {
            int pixel = (y * scene.CaptureWidth + x) * 4;
            source[pixel] = 190; source[pixel + 1] = 80; source[pixel + 2] = 25;
        }
        byte[] target = new byte[160 * 160 * 4];
        int split = scene.PaddingPixels + 80;
        var placements = new[]
        {
            new StudioGlassTile(0, scene.CaptureWidth, scene.CaptureHeight, split, 0, 0, 0, split, scene.CaptureHeight),
            new StudioGlassTile(0, scene.CaptureWidth, scene.CaptureHeight, 0, 0, split, 0, scene.CaptureWidth - split, scene.CaptureHeight)
        };
        renderer.RenderPixelsAsTilesForTesting(source, scene.CaptureWidth, scene.CaptureHeight, placements, new(), target);
        int left = (80 * 160 + 40) * 4, right = (80 * 160 + 120) * 4;
        Assert.Equal(190, target[left]); Assert.Equal(25, target[left + 2]);
        Assert.Equal(20, target[right]); Assert.Equal(180, target[right + 2]);
        Assert.Equal(255, target[left + 3]); Assert.Equal(255, target[right + 3]);
    }

    [Fact]
    public void Snell_edge_refracts_beyond_the_old_seven_pixel_cap_and_separates_channels()
    {
        var material = new GlassMaterial { GlareIntensity = 0, FresnelIntensity = 0, ShadowStrength = 0, BlurRadiusDip = 0 };
        var scene = Circle(material);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = new byte[scene.CaptureWidth * scene.CaptureHeight * 4];
        for (int y = 0; y < scene.CaptureHeight; y++)
        for (int x = 0; x < scene.CaptureWidth; x++)
        {
            int p = (y * scene.CaptureWidth + x) * 4;
            source[p] = source[p + 1] = source[p + 2] = (byte)Math.Clamp(x - scene.PaddingPixels + 40, 0, 255);
            source[p + 3] = 255;
        }
        byte[] target = new byte[160 * 160 * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), target);
        int edge = (80 * 160 + 18) * 4;
        Assert.True(Math.Abs(target[edge + 1] - 58) > 7, "Native edge must exceed the former seven-pixel displacement cap.");
        Assert.True(Math.Abs(target[edge] - target[edge + 2]) >= 3, "Red and blue must sample distinct displaced coordinates.");
    }

    [Fact]
    public void Pinned_default_blurEdge_true_uses_the_diffuse_sample_even_at_the_outer_shoulder()
    {
        var material = new GlassMaterial { GlareIntensity = 0, FresnelIntensity = 0, ShadowStrength = 0, BlurRadiusDip = 5 };
        var scene = Circle(material);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        int edge = (80 * 160 + 18) * 4;
        double distance = scene.Field[edge + 2];
        double displacement = StudioGlassMath.SnellDisplacementDip(distance);
        double fieldLength = Math.Sqrt(scene.Field[edge] * scene.Field[edge] + scene.Field[edge + 1] * scene.Field[edge + 1]);
        double sampleX = scene.PaddingPixels + 18 + displacement * scene.Field[edge] / fieldLength;
        int stripe = (int)Math.Round(sampleX);
        byte[] source = Solid(scene.CaptureWidth, scene.CaptureHeight, 0, 0, 0);
        for (int y = 0; y < scene.CaptureHeight; y++)
        {
            int p = (y * scene.CaptureWidth + stripe) * 4;
            source[p] = source[p + 1] = source[p + 2] = 255;
        }
        byte[] target = new byte[160 * 160 * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), target);
        float[] kernel = StudioGlassMath.GaussianKernel(5);
        double expected = 0;
        for (int offset = -5; offset <= 5; offset++)
            expected += kernel[offset + 5] * Math.Max(0, 1 - Math.Abs(sampleX - stripe - offset));
        int value = (int)Math.Round(expected * 255);
        Assert.InRange(target[edge + 1], value - 2, value + 2);
    }

    [Fact]
    public void Accent_changes_material_without_replacing_the_clear_interior_and_new_frames_update()
    {
        var scene = Circle();
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Solid(scene.CaptureWidth, scene.CaptureHeight, 40, 80, 110);
        byte[] ordinary = new byte[160 * 160 * 4], selected = new byte[ordinary.Length], changed = new byte[ordinary.Length];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), ordinary);
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(AccentRegion: 1), selected);
        renderer.RenderPixels(Solid(scene.CaptureWidth, scene.CaptureHeight, 180, 100, 30), scene.CaptureWidth, scene.CaptureHeight,
            0, 0, new(AccentRegion: 1), changed);
        int center = (80 * 160 + 80) * 4;
        Assert.True(selected[center + 2] > ordinary[center + 2] + 50);
        Assert.True(Math.Abs(changed[center] - selected[center]) > 20);
        Assert.Equal(255, selected[center + 3]);
    }

    [Fact]
    public void Zero_optical_ranges_and_clamped_monitor_edges_produce_finite_valid_pixels()
    {
        var scene = Circle(new GlassMaterial { FresnelRange = 0, GlareRange = 0, BlurRadiusDip = 0 });
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] target = new byte[160 * 160 * 4];
        renderer.RenderPixels(Solid(32, 32, 49, 101, 151), 32, 32, -100, -100, new(Illumination: 1), target);
        Assert.Equal(255, target[(80 * 160 + 80) * 4 + 3]);
        Assert.Contains(target, p => p > 10);
    }

    [Fact]
    public void Hardware_pipeline_reports_settled_frame_cost_without_blocking_a_UI_dispatcher()
    {
        var scene = Circle();
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene);
        byte[] source = Solid(scene.CaptureWidth, scene.CaptureHeight, 73, 129, 181);
        byte[] target = new byte[160 * 160 * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), target);
        var clock = Stopwatch.StartNew();
        for (int frame = 0; frame < 10; frame++) renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(Illumination: frame / 10d), target);
        output.WriteLine($"Native 160x160 four-pass hardware pipeline including synthetic upload and final readback: {clock.Elapsed.TotalMilliseconds / 10:0.00} ms/frame.");
        Assert.Equal(255, target[(80 * 160 + 80) * 4 + 3]);
    }

    [Fact]
    public void Separable_blur_matches_the_pinned_Studio_Gaussian_in_a_clear_interior()
    {
        var scene = Circle(new GlassMaterial { GlareIntensity = 0, FresnelIntensity = 0, ShadowStrength = 0 });
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = Solid(scene.CaptureWidth, scene.CaptureHeight, 0, 0, 0);
        int impulse = ((scene.PaddingPixels + 80) * scene.CaptureWidth + scene.PaddingPixels + 80) * 4;
        source[impulse] = source[impulse + 1] = source[impulse + 2] = 255;
        byte[] target = new byte[160 * 160 * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), target);
        // Upstream radius1 uses sigma=1/3, weight0=1/(1+2*exp(-4.5)).
        double weight0 = 1 / (1 + 2 * Math.Exp(-4.5));
        int expectedCenter = (int)Math.Round(255 * weight0 * weight0);
        Assert.InRange(target[(80 * 160 + 80) * 4], expectedCenter - 1, expectedCenter + 1);
        Assert.InRange(target[(80 * 160 + 81) * 4], 2, 4);
        float[] background = renderer.ReadIntermediateForTesting(StudioPass.Background);
        float[] horizontal = renderer.ReadIntermediateForTesting(StudioPass.GaussianX);
        float[] vertical = renderer.ReadIntermediateForTesting(StudioPass.GaussianY);
        int sourceCenter = ((scene.PaddingPixels + 80) * scene.CaptureWidth + scene.PaddingPixels + 80) * 4;
        Assert.Equal(1, background[sourceCenter]);
        Assert.InRange(horizontal[sourceCenter], (float)weight0 - .001f, (float)weight0 + .001f);
        Assert.InRange(vertical[sourceCenter], (float)(weight0 * weight0) - .001f, (float)(weight0 * weight0) + .001f);
        Assert.Equal(0, horizontal[sourceCenter + scene.CaptureWidth * 4]);
        Assert.True(vertical[sourceCenter + scene.CaptureWidth * 4] > .01f);
    }

    [Fact]
    public void Background_pass_preserves_Studio_shadow_strength_in_floating_point()
    {
        var material = new GlassMaterial { ShadowExpandDip = 2, ShadowStrength = .5, ShadowOffsetYDip = 0 };
        var scene = new GlassScene(100, 100, 1, [GlassRegion.RoundedRect(1, 20, 20, 60, 60, 12)], material);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        renderer.RenderPixels(Solid(scene.CaptureWidth, scene.CaptureHeight, 128, 128, 128), scene.CaptureWidth, scene.CaptureHeight,
            0, 0, new(), new byte[100 * 100 * 4]);
        float[] pixels = renderer.ReadIntermediateForTesting(StudioPass.Background);
        int edge = ((scene.PaddingPixels + 50) * scene.CaptureWidth + scene.PaddingPixels + 20) * 4;
        double expected = 128 / 255d - Math.Exp(-.5 / 2) * .6 * .5;
        Assert.InRange(pixels[edge], (float)expected - .001f, (float)expected + .001f);
        Assert.Equal(1, pixels[edge + 3]);
    }

    [Fact]
    public void Full_size_wheel_reports_GPU_submission_and_completed_readback_cost()
    {
        var scene = GlassScene.WheelPresentation(7, 1, true);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene);
        byte[] source = Solid(scene.CaptureWidth, scene.CaptureHeight, 73, 129, 181);
        byte[] target = new byte[scene.PixelWidth * scene.PixelHeight * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), target);
        double render = 0, readback = 0;
        for (int frame = 0; frame < 20; frame++)
        {
            renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(Illumination: frame / 20d), target);
            render += renderer.LastRenderMilliseconds; readback += renderer.LastReadbackMilliseconds;
        }
        output.WriteLine($"Native {scene.PixelWidth}x{scene.PixelHeight}, padded {scene.CaptureWidth}x{scene.CaptureHeight}: " +
            $"GPU submission {render / 20:0.00} ms; completed final readback {readback / 20:0.00} ms; combined {(render + readback) / 20:0.00} ms. " +
            "Readback includes GPU completion; these are not GPU timestamp queries or full UI presentation timings.");
        Assert.Contains(target, channel => channel == 255);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Calibration_shapes_generate_owned_checkerboard_optical_evidence(bool rectangle)
    {
        const int canvas = 600;
        GlassRegion region = rectangle ? GlassRegion.RoundedRect(1, 180, 205, 240, 190, 45) : GlassRegion.Circle(1, 300, 300, 100);
        var scene = new GlassScene(canvas, canvas, 1, [region], GlassMaterial.StudioCalibration);
        using var renderer = D3D11StudioRenderer.CreateForTesting(scene, software: true);
        byte[] source = new byte[scene.CaptureWidth * scene.CaptureHeight * 4];
        for (int y = 0; y < scene.CaptureHeight; y++)
        for (int x = 0; x < scene.CaptureWidth; x++)
        {
            int p = (y * scene.CaptureWidth + x) * 4;
            byte value = (byte)(((int)Math.Floor((x - scene.PaddingPixels) / 20d) +
                (int)Math.Floor((y - scene.PaddingPixels) / 20d)) % 2 == 0 ? 255 : 191);
            source[p] = source[p + 1] = source[p + 2] = value; source[p + 3] = 255;
        }
        byte[] target = new byte[canvas * canvas * 4];
        renderer.RenderPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, new(), target);
        string folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "studio-optics");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, rectangle ? "studio-rounded-rectangle.png" : "studio-circle.png");
        Save(path, target);
        byte[] composited = new byte[target.Length];
        for (int y = 0; y < canvas; y++)
        for (int x = 0; x < canvas; x++)
        {
            int pixel = (y * canvas + x) * 4;
            int backgroundPixel = ((y + scene.PaddingPixels) * scene.CaptureWidth + x + scene.PaddingPixels) * 4;
            int inverseAlpha = 255 - target[pixel + 3];
            for (int channel = 0; channel < 3; channel++)
                composited[pixel + channel] = (byte)Math.Min(255, target[pixel + channel] + (source[backgroundPixel + channel] * inverseAlpha + 127) / 255);
            composited[pixel + 3] = 255;
        }
        string compositedPath = Path.Combine(folder, rectangle ? "studio-rounded-rectangle-composited.png" : "studio-circle-composited.png");
        Save(compositedPath, composited);
        output.WriteLine($"Generated checkerboard only; no desktop pixels persisted. {path}");
        output.WriteLine($"600DIP reference canvas, matching checkerboard composite: {compositedPath}");
        Assert.Equal(255, target[(300 * canvas + 300) * 4 + 3]);
        Assert.Equal(0, target[3]);

        static void Save(string path, byte[] pixels)
        {
            var image = BitmapSource.Create(canvas, canvas, 96, 96, PixelFormats.Pbgra32, null, pixels, canvas * 4);
            image.Freeze();
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(path); png.Save(file);
        }
    }

    private static void SavePng(string path, byte[] pixels, int width, int height)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        image.Freeze();
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); png.Save(file);
    }

    private static GlassScene Circle(GlassMaterial? material = null) => new(160, 160, 1,
        [GlassRegion.Circle(1, 80, 80, 64)], material ?? new GlassMaterial());

    private static byte[] Solid(int width, int height, byte b, byte g, byte r)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int p = 0; p < pixels.Length; p += 4) { pixels[p] = b; pixels[p + 1] = g; pixels[p + 2] = r; pixels[p + 3] = 255; }
        return pixels;
    }

    private static double Luminance(byte red, byte green, byte blue)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(red) + .7152 * Linear(green) + .0722 * Linear(blue);
    }
}
