namespace Citrus.Windows.Presentation;

/// <summary>Optical parameters calibrated against Liquid Glass Studio f7b28c3.</summary>
internal sealed record GlassMaterial
{
    public double ThicknessDip { get; init; } = 20;
    public double RefractionIndex { get; init; } = 1.4;
    public double RefractionDistance { get; init; } = .05;
    public double Dispersion { get; init; } = 7;
    public double BlurRadiusDip { get; init; } = 1;
    public double TintRed { get; init; }
    public double TintGreen { get; init; }
    public double TintBlue { get; init; }
    public double TintAlpha { get; init; }
    public double DarkTintRed { get; init; }
    public double DarkTintGreen { get; init; }
    public double DarkTintBlue { get; init; }
    public double DarkTintAlpha { get; init; }
    public double SelectionTintOpacity { get; init; } = .72;
    // Blend display-bounded optical highlights over the refracted/frosted body.
    public double EdgeHighlightOpacity { get; init; } = 1;
    public double EdgeFeatherDip { get; init; }
    public double FresnelRange { get; init; } = 30;
    public double FresnelHardness { get; init; } = .2;
    public double FresnelIntensity { get; init; } = .2;
    public double GlareRange { get; init; } = 30;
    public double GlareHardness { get; init; } = .2;
    public double GlareIntensity { get; init; } = .9;
    public double GlareConvergence { get; init; } = .5;
    public double GlareOpposite { get; init; } = .8;
    public double GlareAngleRadians { get; init; } = -Math.PI / 4;
    public double ShadowExpandDip { get; init; } = 1.2;
    public double ShadowStrength { get; init; } = .15;
    public double ShadowOffsetXDip { get; init; }
    public double ShadowOffsetYDip { get; init; } = 1;
    // Optical shadows may be broad. Tight native windows need an independent
    // compact exterior shadow so their presentation rectangle never becomes visible.
    public double? ExteriorShadowExpandDip { get; init; }
    public double? ExteriorShadowOffsetXDip { get; init; }
    public double? ExteriorShadowOffsetYDip { get; init; }
    internal double EffectiveExteriorShadowExpandDip => ExteriorShadowExpandDip ?? ShadowExpandDip;
    internal double EffectiveExteriorShadowOffsetXDip => ExteriorShadowOffsetXDip ?? ShadowOffsetXDip;
    internal double EffectiveExteriorShadowOffsetYDip => ExteriorShadowOffsetYDip ?? ShadowOffsetYDip;
    public static GlassMaterial StudioCalibration { get; } = new() { ShadowExpandDip = 25, ShadowOffsetYDip = 10 };
    // User-supplied Studio screenshot preset, with additional white frosting.
    // Studio's percentage controls map to fractions; UI shadow Y=-10 maps to native +10.
    // The default and Studio calibration remain untinted with one-DIP Gaussian support.
    public static GlassMaterial Citrus { get; } = new()
    {
        ThicknessDip = 33.5, RefractionDistance = .07, RefractionIndex = 1.66, Dispersion = 9.08,
        FresnelRange = 73.75, FresnelHardness = .1298, FresnelIntensity = .5958,
        GlareRange = 22.48, GlareHardness = .1333, GlareIntensity = .7105,
        GlareConvergence = .575, GlareOpposite = 1, GlareAngleRadians = -64 * Math.PI / 180,
        BlurRadiusDip = 23,
        TintRed = 1, TintGreen = 1, TintBlue = 1, TintAlpha = .68,
        DarkTintRed = 17 / 255d, DarkTintGreen = 19 / 255d, DarkTintBlue = 22 / 255d, DarkTintAlpha = .64,
        ShadowExpandDip = 20.04, ShadowStrength = .1417, ShadowOffsetXDip = 0, ShadowOffsetYDip = 10,
        ExteriorShadowExpandDip = 1.2, ExteriorShadowOffsetXDip = 0, ExteriorShadowOffsetYDip = 1
    };
    public static GlassMaterial Wheel { get; } = Citrus with
    {
        FresnelIntensity = Citrus.FresnelIntensity * .7,
        GlareIntensity = Citrus.GlareIntensity * .7,
        // The wheel's individual contours already separate its layers. A black
        // contact shadow turns their antialiasing into a visible hairline.
        ShadowStrength = 0,
        EdgeHighlightOpacity = .10,
        EdgeFeatherDip = 1,
        // A pale neutral undercoat preserves the reference's circle on white desktops.
        TintRed = 225 / 255d, TintGreen = 232 / 255d, TintBlue = 232 / 255d,
        DarkTintRed = 225 / 255d, DarkTintGreen = 232 / 255d, DarkTintBlue = 232 / 255d,
        DarkTintAlpha = Citrus.TintAlpha,
        SelectionTintOpacity = .94
    };
    internal void Validate()
    {
        Range(ThicknessDip, 0, 512, nameof(ThicknessDip));
        Range(RefractionIndex, 1, 4, nameof(RefractionIndex));
        Range(RefractionDistance, 0, 1, nameof(RefractionDistance));
        Range(Dispersion, 0, 50, nameof(Dispersion));
        Range(BlurRadiusDip, 0, 200, nameof(BlurRadiusDip));
        Range(TintRed, 0, 1, nameof(TintRed));
        Range(TintGreen, 0, 1, nameof(TintGreen));
        Range(TintBlue, 0, 1, nameof(TintBlue));
        Range(TintAlpha, 0, 1, nameof(TintAlpha));
        Range(DarkTintRed, 0, 1, nameof(DarkTintRed));
        Range(DarkTintGreen, 0, 1, nameof(DarkTintGreen));
        Range(DarkTintBlue, 0, 1, nameof(DarkTintBlue));
        Range(DarkTintAlpha, 0, 1, nameof(DarkTintAlpha));
        Range(SelectionTintOpacity, 0, 1, nameof(SelectionTintOpacity));
        Range(EdgeHighlightOpacity, 0, 1, nameof(EdgeHighlightOpacity));
        Range(EdgeFeatherDip, 0, 8, nameof(EdgeFeatherDip));
        Range(FresnelRange, 0, 1000, nameof(FresnelRange));
        Range(FresnelHardness, 0, 4, nameof(FresnelHardness));
        Range(FresnelIntensity, 0, 4, nameof(FresnelIntensity));
        Range(GlareRange, 0, 1000, nameof(GlareRange));
        Range(GlareHardness, 0, 4, nameof(GlareHardness));
        Range(GlareIntensity, 0, 4, nameof(GlareIntensity));
        Range(GlareConvergence, 0, 4, nameof(GlareConvergence));
        Range(GlareOpposite, 0, 4, nameof(GlareOpposite));
        Range(GlareAngleRadians, -Math.PI * 64, Math.PI * 64, nameof(GlareAngleRadians));
        Range(ShadowExpandDip, 0, 512, nameof(ShadowExpandDip));
        Range(ShadowStrength, 0, 1, nameof(ShadowStrength));
        Range(ShadowOffsetXDip, -1024, 1024, nameof(ShadowOffsetXDip));
        Range(ShadowOffsetYDip, -1024, 1024, nameof(ShadowOffsetYDip));
        Range(EffectiveExteriorShadowExpandDip, 0, 512, nameof(ExteriorShadowExpandDip));
        Range(EffectiveExteriorShadowOffsetXDip, -1024, 1024, nameof(ExteriorShadowOffsetXDip));
        Range(EffectiveExteriorShadowOffsetYDip, -1024, 1024, nameof(ExteriorShadowOffsetYDip));
    }

    private static void Range(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"Expected a finite value from {minimum} to {maximum}.");
    }
}
