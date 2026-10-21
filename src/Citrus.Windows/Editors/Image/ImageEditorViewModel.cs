using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Image;

public sealed record AdjustmentField(string Property, string Label, double Minimum, double Maximum, double Step)
{
    public double Read(ImageAdjustments value) => (double)typeof(ImageAdjustments).GetProperty(Property)!.GetValue(value)! - (Property == nameof(value.Gamma) ? 1 : 0);
    public ImageAdjustments Write(ImageAdjustments a, double value) => Property switch
    {
        nameof(a.ExposureEv) => a with { ExposureEv = value }, nameof(a.Brightness) => a with { Brightness = value },
        nameof(a.Contrast) => a with { Contrast = value }, nameof(a.Gamma) => a with { Gamma = value == -.9 ? .1 : value + 1 },
        nameof(a.Highlights) => a with { Highlights = value }, nameof(a.Shadows) => a with { Shadows = value },
        nameof(a.Whites) => a with { Whites = value }, nameof(a.Blacks) => a with { Blacks = value },
        nameof(a.Saturation) => a with { Saturation = value }, nameof(a.Vibrance) => a with { Vibrance = value },
        nameof(a.HueDegrees) => a with { HueDegrees = value }, nameof(a.Warmth) => a with { Warmth = value },
        nameof(a.Tint) => a with { Tint = value }, nameof(a.Clarity) => a with { Clarity = value },
        nameof(a.Dehaze) => a with { Dehaze = value }, nameof(a.Sharpen) => a with { Sharpen = value },
        nameof(a.Denoise) => a with { Denoise = value }, nameof(a.BlurSigma) => a with { BlurSigma = value },
        nameof(a.Vignette) => a with { Vignette = value }, nameof(a.Grain) => a with { Grain = value },
        nameof(a.EffectAmount) => a with { EffectAmount = value }, _ => throw new ArgumentException("Unknown adjustment.")
    };
}
public static class ImageEditorViewModel
{
    public static readonly IReadOnlyList<AdjustmentField> Fields = Array.AsReadOnly(new[]
    {
        new AdjustmentField("ExposureEv", "Exposure · EV", -5, 5, .05), new("Brightness", "Brightness", -1, 1, .01),
        new("Contrast", "Contrast", -1, 1, .01),
        new("Highlights", "Highlights", -1, 1, .01), new("Shadows", "Shadows", -1, 1, .01),
        new("Whites", "Whites", -1, 1, .01), new("Blacks", "Blacks", -1, 1, .01), new("Gamma", "Gamma", -.9, 4, .05),
        new("Warmth", "Temperature", -1, 1, .01),new("Tint", "Tint", -1, 1, .01),
        new("Saturation", "Saturation", 0, 2, .01), new("Vibrance", "Vibrance", -1, 1, .01),
        new("HueDegrees", "Hue · degrees", -180, 180, 1), new("Clarity", "Clarity", 0, 1, .01),
        new("Dehaze", "Dehaze", 0, 1, .01), new("Sharpen", "Sharpen", 0, 1, .01),
        new("Denoise", "Denoise", 0, 1, .01), new("BlurSigma", "Blur · source pixels", 0, 100, .1),
        new("Vignette", "Vignette", -1, 1, .01), new("Grain", "Grain", 0, 1, .01),
        new("EffectAmount", "Effect amount", 0, 1, .01)
    });
}
