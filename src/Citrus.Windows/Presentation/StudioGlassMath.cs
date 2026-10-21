// Optical formulas adapted from Liquid Glass Studio, MIT, Copyright (c) 2024 Charles Yin.
// Full license and source pin: packaging/notices/presentation/Liquid-Glass-Studio-LICENSE.txt.
namespace Citrus.Windows.Presentation;

internal static class StudioGlassMath
{
    // Studio's SDF normal is multiplied by sqrt(2)*1000 before its normalized-UV
    // sampling offset. Express that same displacement in DIP, independent of ROI padding.
    private const double NormalizationDip = 1414.2135623730951;

    public static double SnellDisplacementDip(double inwardDistanceDip, double thicknessDip = 20,
        double refractionIndex = 1.4, double refractionDistance = .05)
    {
        if (!double.IsFinite(inwardDistanceDip) || !double.IsFinite(thicknessDip) || thicknessDip <= 0 ||
            !double.IsFinite(refractionIndex) || refractionIndex <= 1 ||
            !double.IsFinite(refractionDistance) || refractionDistance <= 0 || inwardDistanceDip >= thicknessDip)
            return 0;
        double shoulder = Math.Clamp(1 - Math.Max(0, inwardDistanceDip) / thicknessDip, 0, 1);
        double thetaI = Math.Asin(Math.Clamp(shoulder * shoulder, -1, 1));
        double thetaT = Math.Asin(Math.Clamp(Math.Sin(thetaI) / refractionIndex, -1, 1));
        double value = Math.Tan(thetaI - thetaT) * refractionDistance * NormalizationDip;
        return double.IsFinite(value) ? Math.Max(0, value) : 0;
    }

    public static double MaximumDisplacementDip(GlassMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        material.Validate();
        return SnellDisplacementDip(0, material.ThicknessDip, material.RefractionIndex, material.RefractionDistance)
            * (1 + .02 * material.Dispersion);
    }

    public static int CapturePaddingPixels(GlassMaterial material, double scale)
    {
        ArgumentNullException.ThrowIfNull(material);
        material.Validate();
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        // Blur support is the source radius, with sigma = radius/3. Shadows are
        // finite here at the first distance below 1/1024 intensity.
        double blurSupport = Math.Ceiling(material.BlurRadiusDip * scale);
        double ShadowSupport(double expand, double x, double y) => material.ShadowStrength <= 1d / (1024 * .6) || expand == 0
            ? 0 : expand * Math.Log(.6 * material.ShadowStrength * 1024) + Math.Max(Math.Abs(x), Math.Abs(y));
        double shadowSupportDip = Math.Max(ShadowSupport(material.ShadowExpandDip, material.ShadowOffsetXDip, material.ShadowOffsetYDip),
            ShadowSupport(material.EffectiveExteriorShadowExpandDip, material.EffectiveExteriorShadowOffsetXDip, material.EffectiveExteriorShadowOffsetYDip));
        double support = Math.Max(MaximumDisplacementDip(material) * scale + blurSupport,
            shadowSupportDip * scale + blurSupport);
        if (!double.IsFinite(support) || support > 8190) throw new ArgumentOutOfRangeException(nameof(material));
        return checked((int)Math.Ceiling(support) + 2);
    }

    /// <summary>Full symmetric source Gaussian kernel, including both negative and positive offsets.</summary>
    public static float[] GaussianKernel(double radiusPixels)
    {
        if (!double.IsFinite(radiusPixels) || radiusPixels < 0 || radiusPixels > 4096)
            throw new ArgumentOutOfRangeException(nameof(radiusPixels));
        if (radiusPixels == 0) return [1];
        int radius = (int)Math.Ceiling(radiusPixels);
        double sigma = radiusPixels / 3;
        var weights = new float[radius * 2 + 1];
        double sum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            double normalized = i / sigma;
            double weight = i == 0 ? 1 : Math.Exp(-.5 * normalized * normalized);
            weights[i + radius] = (float)weight;
            sum += weight;
        }
        for (int i = 0; i < weights.Length; i++) weights[i] = (float)(weights[i] / sum);
        return weights;
    }

    public static double Fresnel(double inwardDistanceDip, double range, double hardness, double intensity)
    {
        if (!double.IsFinite(inwardDistanceDip) || !double.IsFinite(range) || range <= 0 ||
            !double.IsFinite(hardness) || !double.IsFinite(intensity) || intensity <= 0)
            return 0;
        double depth = Math.Max(0, inwardDistanceDip);
        // A tiny positive range may overflow its squared reciprocal. The exact rim
        // still has factor1, not the NaN produced by multiplying zero by infinity.
        double powerBase = depth == 0 ? Math.Max(0, 1 + hardness)
            : Math.Max(0, 1 - depth / 1500 * Math.Pow(500 / range, 2) + hardness);
        return Math.Clamp(Math.Pow(powerBase, 5), 0, 1) * Math.Max(0, intensity);
    }

    public static double Glare(double inwardDistanceDip, double normalX, double normalY, GlassMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!double.IsFinite(normalX) || !double.IsFinite(normalY) ||
            !double.IsFinite(material.GlareAngleRadians) || !double.IsFinite(material.GlareConvergence) ||
            !double.IsFinite(material.GlareOpposite) || !double.IsFinite(material.GlareIntensity) ||
            material.GlareIntensity <= 0 || (normalX == 0 && normalY == 0)) return 0;
        // Field normals point inward in top-left screen coordinates; Studio's
        // angular model uses an outward normal in bottom-left shader coordinates.
        double normalAngle = Math.Atan2(normalY, -normalX);
        if (normalAngle < 0) normalAngle += Math.PI * 2;
        double angle = (normalAngle - Math.PI / 4 + material.GlareAngleRadians) * 2;
        bool farSide = (angle > Math.PI * 1.5 && angle < Math.PI * 3.5) || angle < -Math.PI * .5;
        double angularBase = Math.Max(0, (.5 + Math.Sin(angle) * .5) * 1.2 *
            (farSide ? Math.Max(0, material.GlareOpposite) : 1) * material.GlareIntensity);
        double angular = Math.Clamp(Math.Pow(angularBase, Math.Max(.01, .1 + material.GlareConvergence * 2)), 0, 1);
        double geometric = Fresnel(inwardDistanceDip, material.GlareRange, material.GlareHardness, 1);
        double result = angular * geometric;
        return double.IsFinite(result) ? result : 0;
    }
}
