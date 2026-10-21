using Citrus.Core;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public class StudioGlassSceneTests
{
    [Fact]
    public void Default_constructed_frame_is_visible_and_has_no_pointer()
    {
        var state = new GlassFrameState();
        Assert.Equal(1, state.Materialize);
        Assert.True(double.IsNaN(state.PointerXDip));
        Assert.True(double.IsNaN(state.PointerYDip));
    }

    [Fact]
    public void Circle_field_has_signed_distance_and_inward_normal_at_pixel_centers()
    {
        var scene = new GlassScene(100, 80, 1, [GlassRegion.Circle(4, 50, 40, 30)]);
        Assert.Equal(100 * 80 * 4, scene.Field.Length);
        var inside = Read(scene, 79, 40);
        Assert.InRange(inside[0], -1, -.999);
        Assert.InRange(inside[2], .49f, .51f);
        Assert.Equal(4, inside[3]);
        Assert.True(Read(scene, 90, 40)[2] < 0);
    }

    [Fact]
    public void Later_regions_override_only_inside_their_own_contour()
    {
        var scene = new GlassScene(50, 50, 1,
            [GlassRegion.RoundedRect(1, 1, 1, 48, 48, 8), GlassRegion.Circle(2, 25, 25, 5)]);
        Assert.Equal(2, Read(scene, 25, 25)[3]);
        Assert.Equal(1, Read(scene, 32, 25)[3]);
        Assert.True(Read(scene, 0, 0)[2] < 0);
        Assert.Equal(4.5, scene.ShoulderThicknessDip(2));
        Assert.Equal(Math.Min(GlassMaterial.Citrus.ThicknessDip, .45 * 48), scene.ShoulderThicknessDip(1));
    }

    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    [InlineData(5)][InlineData(6)][InlineData(7)][InlineData(8)][InlineData(9)]
    public void Wheel_uses_a_continuous_field_beneath_every_foreground_petal(int count)
    {
        var scene = GlassScene.Wheel(count, 1);
        Assert.Equal(368, scene.PixelWidth);
        Assert.Equal(368, scene.PixelHeight);
        Assert.Single(scene.Regions);
        Assert.Equal(1, Read(scene, 184, 184)[3]);
        for (int y = 0; y < 368; y += 3)
        for (int x = 0; x < 368; x += 3)
        {
            int petal = WheelGeometry.HitTest(x + .5, y + .5, count);
            if (petal >= 0) Assert.Equal(1, Read(scene, x, y)[3]);
        }
        var gap = WheelGeometry.At(120, 180d / count);
        Assert.Equal(1, Read(scene, (int)gap.X, (int)gap.Y)[3]);
        Assert.True(Read(scene, 0, 0)[2] < 0);
    }

    [Fact]
    public void Polygon_factory_copies_caller_outline_and_uses_exact_segments()
    {
        var points = new[] { new WheelPoint(5, 5), new WheelPoint(45, 5), new WheelPoint(25, 45) };
        var region = GlassRegion.Polygon(7, points);
        points[0] = new(500, 500);
        var scene = new GlassScene(50, 50, 1, [region]);
        Assert.Equal(7, Read(scene, 25, 15)[3]);
        Assert.InRange(Read(scene, 25, 5)[2], .4999f, .5001f);
        Assert.True(Read(scene, 45, 40)[2] < 0);
    }

    [Fact]
    public void Studio_refraction_is_nonlinear_and_padding_includes_red_channel()
    {
        double edge = StudioGlassMath.SnellDisplacementDip(0);
        Assert.InRange(edge, 69.28, 69.29);
        Assert.True(StudioGlassMath.SnellDisplacementDip(10) < edge / 4);
        Assert.Equal(0, StudioGlassMath.SnellDisplacementDip(20));
        Assert.InRange(StudioGlassMath.MaximumDisplacementDip(new()), 78.98, 78.99);
        var scene = new GlassScene(100, 60, 2, [GlassRegion.Circle(1, 30, 30, 25)]);
        Assert.True(scene.PaddingPixels >= 162);
        Assert.Equal(200 + scene.PaddingPixels * 2, scene.CaptureWidth);
        Assert.Equal(120 + scene.PaddingPixels * 2, scene.CaptureHeight);
    }

    [Fact]
    public void Gaussian_is_normalized_symmetric_and_zero_radius_is_identity()
    {
        float[] kernel = StudioGlassMath.GaussianKernel(2);
        Assert.Equal(1, kernel.Sum(), 5);
        Assert.Equal(kernel, kernel.Reverse());
        Assert.True(kernel[kernel.Length / 2] > kernel[0]);
        Assert.Equal([1f], StudioGlassMath.GaussianKernel(0));
        Assert.InRange(StudioGlassMath.GaussianKernel(1)[1], .97826f, .97827f);
    }

    [Fact]
    public void Degenerate_optical_values_are_finite_and_scene_rejects_invalid_materials()
    {
        Assert.Equal(0, StudioGlassMath.SnellDisplacementDip(1, 0));
        Assert.Equal(0, StudioGlassMath.SnellDisplacementDip(double.NaN));
        Assert.Equal(0, StudioGlassMath.Fresnel(0, 0, .2, .2));
        Assert.Equal(0, StudioGlassMath.Fresnel(100, 30, .2, .2));
        Assert.True(double.IsFinite(StudioGlassMath.Fresnel(0, double.Epsilon, .2, .2)));
        Assert.True(double.IsFinite(StudioGlassMath.Glare(0, 0, 0, new())));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(100, 100, 1,
            [GlassRegion.Circle(1, 50, 50, 20)], new() { RefractionIndex = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(100, 100, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(double.PositiveInfinity, 100, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(100_000, 100_000, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlassScene(100, 100, 2, [], new() { BlurRadiusDip = 101 }));
    }

    [Fact]
    public void Scene_copies_region_list_and_rejects_ambiguous_identifiers()
    {
        var regions = new List<GlassRegion> { GlassRegion.Circle(1, 50, 50, 20) };
        var scene = new GlassScene(100, 100, 1, regions);
        regions.Clear();
        Assert.Single(scene.Regions);
        Assert.Throws<ArgumentException>(() => new GlassScene(100, 100, 1,
            [GlassRegion.Circle(1, 20, 20, 10), GlassRegion.Circle(1, 70, 70, 10)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlassScene.Wheel(10, 1));
    }

    [Theory]
    [InlineData(0, false, 2)]
    [InlineData(0, true, 2)]
    [InlineData(7, false, 2)]
    [InlineData(8, true, 2)]
    [InlineData(9, true, 2)]
    public void Production_scene_includes_preparing_and_navigation_without_extending_wheel_lighting(int count, bool navigation, int regions)
    {
        var scene = GlassScene.WheelPresentation(count, 1, navigation);
        Assert.Equal(368, scene.PixelWidth);
        Assert.Equal(454, scene.PixelHeight);
        Assert.Equal(regions, scene.Regions.Count);
        Assert.Equal(368, scene.LightingReferenceHeightDip);
        Assert.Equal(184, scene.LightingCenterXDip);
        Assert.Equal(184, scene.LightingCenterYDip);
        Assert.Contains(scene.Regions, region => region.Id == 32);
        Assert.Equal(1, Read(scene, 184, 184)[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => GlassScene.WheelPresentation(-1, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlassScene.WheelPresentation(10, 1, false));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Navigation_field_keeps_existing_capsule_bounds_and_transparent_corners_at_every_DPI(double scale)
    {
        var scene = GlassScene.WheelPresentation(3, scale, true);
        GlassRegion navigation = Assert.Single(scene.Regions, region => region.Id == 32);
        Assert.Equal(new GlassRect(86, 372, 196, 36), navigation.Bounds);
        Assert.Equal((int)Math.Ceiling(368 * scale), scene.PixelWidth);
        Assert.Equal((int)Math.Ceiling(454 * scale), scene.PixelHeight);
        Assert.Equal(32, Read(scene, (int)(184 * scale), (int)(390 * scale))[3]);
        Assert.True(Read(scene, (int)(184 * scale), (int)(390 * scale))[2] > 17);
        Assert.True(Read(scene, (int)(86 * scale), (int)(372 * scale))[2] < 0);
        Assert.True(Read(scene, (int)(184 * scale), (int)(453 * scale))[2] < 0);
        var unpaged = GlassScene.WheelPresentation(3, scale, false);
        Assert.Equal(Read(scene, (int)(184 * scale), (int)(390 * scale)),
            Read(unpaged, (int)(184 * scale), (int)(390 * scale)));
    }

    [Fact]
    public void Generic_scene_lighting_uses_its_actual_dimensions()
    {
        var scene = new GlassScene(100, 80, 1, []);
        Assert.Equal(80, scene.LightingReferenceHeightDip);
        Assert.Equal(50, scene.LightingCenterXDip);
        Assert.Equal(40, scene.LightingCenterYDip);
    }

    private static float[] Read(GlassScene scene, int x, int y) => scene.Field[(4 * (y * scene.PixelWidth + x))..(4 * (y * scene.PixelWidth + x) + 4)];
}
