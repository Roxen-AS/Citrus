using Citrus.Core.Editing;
using Citrus.Conversion.Editing.Image;
using ImageMagick;

namespace Citrus.Conversion.Tests;

public sealed class ImageScaledKernelTests
{
    private static byte Q(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    private static double L(byte[] p, int i) => .2126 * p[i] + .7152 * p[i + 1] + .0722 * p[i + 2];
    private static byte[] Pattern(int width, int height)
    {
        var p = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) { int i = (y * width + x) * 4; p[i] = (byte)((x * 37 + y * 19) % 256); p[i + 1] = (byte)((x * 11 + y * 43) % 256); p[i + 2] = (byte)((x * 67 + y * 7) % 256); p[i + 3] = 255; }
        return p;
    }
    private static byte[] Render(byte[] pixels, int width, int height, ImageAdjustments a, double scale)
    {
        using var image = new MagickImage(pixels, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
        var recipe = new ImageRecipe(Guid.NewGuid(), new(0, false, false, null, null, false), a, [], null, [], []);
        if (scale == 1) ImageAdjustmentsPipeline.Apply(image, recipe, new(width, height), default);
        else ImageAdjustmentsPipeline.ApplyScaled(image, recipe, new((int)(width / scale), (int)(height / scale)), default, scale);
        return image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
    }
    [Theory]
    [InlineData("clarity", .25)]
    [InlineData("sharpen", .25)]
    [InlineData("blur", .25)]
    [InlineData("median", .25)]
    [InlineData("median", .5)]
    public void Scaled_spatial_kernels_match_independent_opaque_plane_reference(string control, double scale)
    {
        const int width = 29, height = 23; var original = Pattern(width, height); var expected = (byte[])original.Clone();
        var a = ImagePreviewPerformanceTests.Identity;
        double sigma = control switch { "clarity" => 12 * scale, "sharpen" => scale, _ => 4 * scale };
        var input = (byte[])original.Clone(); if (control == "clarity") for (int i = 0; i < input.Length; i += 4) input[i] = input[i + 1] = input[i + 2] = Q(L(input, i));
        using var reference = new MagickImage(input, new PixelReadSettings(width, height, StorageType.Char, PixelMapping.RGBA)); reference.Alpha(AlphaAction.Off);
        if (control == "median") { uint radius = (uint)Math.Round(3 * scale, MidpointRounding.AwayFromZero); reference.Statistic(StatisticType.Median, 2 * radius + 1, 2 * radius + 1); a = a with { Denoise = 1 }; }
        else reference.GaussianBlur(0, sigma, Channels.RGB);
        var low = reference.GetPixels().ToByteArray(PixelMapping.RGBA)!;
        for (int i = 0; i < expected.Length; i += 4) for (int c = 0; c < 3; c++) expected[i + c] = control switch
        {
            "clarity" => Q(original[i + c] + .6 * (L(original, i) - low[i])),
            "sharpen" => Q(original[i + c] + 1.2 * (original[i + c] - low[i + c])),
            _ => low[i + c]
        };
        a = control switch { "clarity" => a with { Clarity = .6 }, "sharpen" => a with { Sharpen = .6 }, "blur" => a with { BlurSigma = 4 }, _ => a };
        Assert.Equal(expected, Render(original, width, height, a, scale));
    }
    [Theory]
    [InlineData(1, 7)]
    [InlineData(.25, 2)]
    [InlineData(.04, 0)]
    public void Dehaze_histogram_and_scaled_window_match_brute_force_with_multiple_tied_candidates(double scale, int radius)
    {
        const int width = 67, height = 35; var original = Pattern(width, height);
        // Several thousand pixels select three candidates; zero channel values create many
        // tied dark windows, while varying luma determines the atmospheric candidate.
        for (int i = 0; i < original.Length; i += 4) if (i % 28 == 0) original[i + 3] = 0;
        float[] Dark(double ar, double ag, double ab)
        {
            var result = new float[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float min = float.PositiveInfinity;
                for (int yy = Math.Max(0, y - radius); yy <= Math.Min(height - 1, y + radius); yy++) for (int xx = Math.Max(0, x - radius); xx <= Math.Min(width - 1, x + radius); xx++)
                { int j = (yy * width + xx) * 4; if (original[j + 3] == 0) continue; min = Math.Min(min, (float)Math.Min(original[j] / 255d / ar, Math.Min(original[j + 1] / 255d / ag, original[j + 2] / 255d / ab))); }
                result[y * width + x] = float.IsFinite(min) ? min : 0;
            }
            return result;
        }
        var dark = Dark(1, 1, 1); var visible = Enumerable.Range(0, width * height).Where(i => original[i * 4 + 3] != 0).ToArray();
        int count = (int)Math.Ceiling(visible.Length * .001); Assert.True(count > 1);
        int atmospheric = visible.OrderByDescending(i => dark[i]).ThenBy(i => i).Take(count).OrderByDescending(i => L(original, i * 4)).ThenBy(i => i).First() * 4;
        double[] air = Enumerable.Range(0, 3).Select(c => Math.Max(1 / 255d, original[atmospheric + c] / 255d)).ToArray(); dark = Dark(air[0], air[1], air[2]);
        var expected = (byte[])original.Clone();
        for (int n = 0; n < dark.Length; n++) { double t = Math.Max(.1, 1 - .95 * .6 * dark[n]); for (int c = 0; c < 3; c++) expected[n * 4 + c] = Q(Math.Clamp((original[n * 4 + c] / 255d - air[c]) / t + air[c], 0, 1) * 255); }
        Assert.Equal(expected, Render(original, width, height, ImagePreviewPerformanceTests.Identity with { Dehaze = .6 }, scale));
    }
}
