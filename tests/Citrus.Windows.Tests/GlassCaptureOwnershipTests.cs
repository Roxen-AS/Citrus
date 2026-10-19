using Citrus.Core;
using Xunit;

namespace Citrus.Windows.Tests;

public sealed class GlassCaptureOwnershipTests
{
    private static readonly ScreenRect Crop = new(100, 100, 368, 414);
    private static readonly ScreenRect Work = new(0, 0, 1200, 800);
    private static GlassCaptureSnapshot Owned(params GlassCaptureWindow[] extra) => new(Crop, Work, 1, 2, 3,
        [new(2, Crop, true, true), new(3, Crop, true, true), .. extra, new(1, Work, true, true)], true);

    [Fact]
    public void Entire_crop_inside_visible_owned_background_and_work_area_is_allowed()
        => Assert.True(GlassCaptureOwnership.Check(Owned()).Allowed);

    [Fact]
    public void Crop_outside_actual_owned_background_is_refused()
    {
        var scene = Owned() with { Windows = [new(2, Crop, true, true), new(3, Crop, true, true), new(1, new(100, 100, 368, 360), true, true)] };
        Assert.False(GlassCaptureOwnership.Check(scene).Allowed);
    }

    [Fact]
    public void Crop_outside_work_area_is_refused()
        => Assert.False(GlassCaptureOwnership.Check(Owned() with { WorkArea = new(0, 0, 1200, 500) }).Allowed);

    [Fact]
    public void Unrelated_visual_occluder_over_corner_outside_ellipse_is_refused()
        => Assert.False(GlassCaptureOwnership.Check(Owned(new GlassCaptureWindow(99, new(100, 100, 8, 8), true, true))).Allowed);

    [Fact]
    public void Disabled_or_input_transparent_unrelated_occluder_is_still_refused()
    {
        // Visual ownership deliberately has no WindowFromPoint/input-transparent/disabled exemption.
        Assert.False(GlassCaptureOwnership.Check(Owned(new GlassCaptureWindow(99, new(100, 470, 40, 40), true, true))).Allowed);
    }

    [Fact]
    public void Unrelated_window_below_opaque_owned_background_does_not_occlude()
    {
        var scene = Owned();
        Assert.True(GlassCaptureOwnership.Check(scene with { Windows = [.. scene.Windows, new(99, Crop, true, true)] }).Allowed);
    }

    [Fact]
    public void Missing_background_incomplete_snapshot_or_unknown_visible_bounds_is_refused()
    {
        Assert.False(GlassCaptureOwnership.Check(Owned() with { Complete = false }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(Owned() with { Background = 404 }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(Owned(new GlassCaptureWindow(99, default, true, false))).Allowed);
    }

    [Fact]
    public void Companion_above_wheel_is_refused_even_though_input_hit_test_would_skip_it()
    {
        var scene = Owned() with { Windows = [new(3, Crop, true, true), new(2, Crop, true, true), new(1, Work, true, true)] };
        Assert.False(GlassCaptureOwnership.Check(scene).Allowed);
    }
}
