using Citrus.Core.Editing;

namespace Citrus.Core.Tests;

public sealed class ImageRecipeValidationTests
{
    private static EditorDocument Document(ImageAdjustments a)
    {
        Guid id = Guid.NewGuid(); return new(1, 0, [new(id, Path.GetFullPath("ramp.png"), 1, 0, new string('A', 64))], [], new ImageRecipe(id, new(0, false, false, null, null, false), a, [], null, [], []), new("png", null, "edited", new QualityGoal(90), MetadataPolicy.PreserveSupported, new(255, 255, 255, 255)));
    }
    private static readonly ImageAdjustments Identity = new(1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, PhotoEffect.None, 0);
    [Fact]
    public void All_v1_adjustment_bounds_and_nonfinite_values_are_enforced()
    {
        Assert.Empty(EditorRecipeValidator.Validate(Document(Identity)));
        var bounds = new Dictionary<string, (double Min, double Max)>
        {
            [nameof(ImageAdjustments.ExposureEv)] = (-5, 5),
            [nameof(ImageAdjustments.Gamma)] = (.1, 5),
            [nameof(ImageAdjustments.Saturation)] = (0, 2),
            [nameof(ImageAdjustments.HueDegrees)] = (-180, 180),
            [nameof(ImageAdjustments.BlurSigma)] = (0, 100)
        };
        foreach (string name in new[] { "Brightness", "Contrast", "Highlights", "Shadows", "Vibrance", "Warmth", "Tint", "Vignette" }) bounds[name] = (-1, 1);
        foreach (string name in new[] { "Clarity", "Dehaze", "Sharpen", "Denoise", "Grain", "EffectAmount" }) bounds[name] = (0, 1);
        foreach (var (name, range) in bounds)
        {
            var property = typeof(ImageAdjustments).GetProperty(name)!;
            foreach (double value in new[] { range.Min, range.Max }) { var a = Identity with { }; property.SetValue(a, value); Assert.Empty(EditorRecipeValidator.Validate(Document(a))); }
            foreach (double value in new[] { range.Min - .01, range.Max + .01, double.NaN, double.PositiveInfinity }) { var a = Identity with { }; property.SetValue(a, value); Assert.NotEmpty(EditorRecipeValidator.Validate(Document(a))); }
        }
        Assert.NotEmpty(EditorRecipeValidator.Validate(Document(Identity with { AlgorithmVersion = 2 })));
    }
}
