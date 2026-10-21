using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class GlassLabelContrastTests
{
    [Theory]
    [InlineData(255, false)]
    [InlineData(0, true)]
    public void Clear_flat_backdrops_need_no_protective_scrim(byte channel, bool light)
    {
        var pixels = Pixels(channel, channel);
        var contrast = GlassLabelContrast.Evaluate(pixels, 2, 1, 1, new System.Windows.Rect(0, 0, 2, 1));
        Assert.Equal(light, contrast.LightText);
        Assert.Equal(0, contrast.ScrimOpacity);
    }

    [Fact]
    public void Dense_black_and_white_content_gets_local_protection_with_readable_contrast()
    {
        var contrast = GlassLabelContrast.Evaluate(Pixels(0, 255), 2, 1, 1, new System.Windows.Rect(0, 0, 2, 1));
        Assert.InRange(contrast.ScrimOpacity, .35, .65);
        double worstChannel = contrast.LightText ? 1 - contrast.ScrimOpacity : contrast.ScrimOpacity;
        double luminance = worstChannel <= .04045 ? worstChannel / 12.92 : Math.Pow((worstChannel + .055) / 1.055, 2.4);
        Assert.True((contrast.LightText ? 1.05 / (luminance + .05) : (luminance + .05) / .05) >= 4.5);
    }

    private static byte[] Pixels(byte first, byte second) => [first, first, first, 255, second, second, second, 255];
}
