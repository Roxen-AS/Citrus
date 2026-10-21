using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class FrostedMaterialTests
{
    [Fact]
    public void Production_material_matches_the_supplied_Studio_preset_and_adds_white_brightening()
    {
        var material = GlassMaterial.Citrus;
        Assert.Equal(33.5, material.ThicknessDip);
        Assert.Equal(.07, material.RefractionDistance);
        Assert.Equal(1.66, material.RefractionIndex);
        Assert.Equal(9.08, material.Dispersion);
        Assert.Equal(73.75, material.FresnelRange);
        Assert.Equal(.1298, material.FresnelHardness);
        Assert.Equal(.5958, material.FresnelIntensity);
        Assert.Equal(22.48, material.GlareRange);
        Assert.Equal(.1333, material.GlareHardness);
        Assert.Equal(.7105, material.GlareIntensity);
        Assert.Equal(.575, material.GlareConvergence);
        Assert.Equal(1, material.GlareOpposite);
        Assert.Equal(-64 * Math.PI / 180, material.GlareAngleRadians);
        Assert.Equal(23, material.BlurRadiusDip);
        Assert.Equal(20.04, material.ShadowExpandDip);
        Assert.Equal(.1417, material.ShadowStrength);
        Assert.Equal(0, material.ShadowOffsetXDip);
        Assert.Equal(10, material.ShadowOffsetYDip);
        Assert.Equal(1, material.TintRed);
        Assert.Equal(1, material.TintGreen);
        Assert.Equal(1, material.TintBlue);
        Assert.Equal(.68, material.TintAlpha);
        Assert.Equal(17 / 255d, material.DarkTintRed);
        Assert.Equal(19 / 255d, material.DarkTintGreen);
        Assert.Equal(22 / 255d, material.DarkTintBlue);
        Assert.Equal(.64, material.DarkTintAlpha);
    }

    [Fact]
    public void Exterior_shadow_is_compact_and_generic_materials_keep_their_optical_shadow_defaults()
    {
        Assert.Equal(1.2, GlassMaterial.Citrus.EffectiveExteriorShadowExpandDip);
        Assert.Equal(0, GlassMaterial.Citrus.EffectiveExteriorShadowOffsetXDip);
        Assert.Equal(1, GlassMaterial.Citrus.EffectiveExteriorShadowOffsetYDip);
        var generic = new GlassMaterial { ShadowExpandDip = 25, ShadowOffsetXDip = -2, ShadowOffsetYDip = 10 };
        Assert.Equal(25, generic.EffectiveExteriorShadowExpandDip);
        Assert.Equal(-2, generic.EffectiveExteriorShadowOffsetXDip);
        Assert.Equal(10, generic.EffectiveExteriorShadowOffsetYDip);
    }

    [Theory]
    [InlineData(1, 180)]
    [InlineData(1.5, 270)]
    [InlineData(2, 358)]
    public void Stronger_refraction_and_blur_receive_sufficient_padded_sampling(double scale, int padding)
    {
        Assert.InRange(StudioGlassMath.MaximumDisplacementDip(GlassMaterial.Citrus), 154.98, 155);
        Assert.Equal(padding, StudioGlassMath.CapturePaddingPixels(GlassMaterial.Citrus, scale));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-.01)]
    [InlineData(513)]
    public void Exterior_shadow_expansion_rejects_invalid_values(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(10, 10, 1, [], new() { ExteriorShadowExpandDip = value }));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1024.01)]
    [InlineData(1024.01)]
    public void Exterior_shadow_offsets_reject_invalid_values(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(10, 10, 1, [], new() { ExteriorShadowOffsetXDip = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(10, 10, 1, [], new() { ExteriorShadowOffsetYDip = value }));
    }

    [Fact]
    public void Studio_calibration_retains_untinted_one_DIP_support()
    {
        Assert.Equal(1, new GlassMaterial().BlurRadiusDip);
        Assert.Equal(1, GlassMaterial.StudioCalibration.BlurRadiusDip);
        Assert.Equal(0, new GlassMaterial().TintAlpha);
        Assert.Equal(0, new GlassMaterial().DarkTintAlpha);
        Assert.Equal(0, GlassMaterial.StudioCalibration.TintAlpha);
        Assert.Equal(0, GlassMaterial.StudioCalibration.DarkTintAlpha);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-.001)]
    [InlineData(1.001)]
    public void Every_tint_component_rejects_nonfinite_or_out_of_range_values(double invalid)
    {
        GlassMaterial[] invalidMaterials =
        [
            new() { TintRed = invalid }, new() { TintGreen = invalid },
            new() { TintBlue = invalid }, new() { TintAlpha = invalid },
            new() { DarkTintRed = invalid }, new() { DarkTintGreen = invalid },
            new() { DarkTintBlue = invalid }, new() { DarkTintAlpha = invalid },
            new() { SelectionTintOpacity = invalid }
        ];
        foreach (GlassMaterial material in invalidMaterials)
            Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(10, 10, 1, [], material));
    }

    [Fact]
    public void Both_tint_endpoints_are_accepted()
    {
        new GlassScene(10, 10, 1, [], new()
        {
            TintRed = 0, TintGreen = 1, TintBlue = 0, TintAlpha = 1,
            DarkTintRed = 1, DarkTintGreen = 0, DarkTintBlue = 1, DarkTintAlpha = 0
        });
    }
}
