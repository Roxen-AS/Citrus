using Citrus.Core;
using Xunit;

namespace Citrus.Windows.Tests;

public sealed class GlassCaptureOwnershipTests
{
    private static readonly ScreenRect Crop = new(100, 100, 368, 414);
    private static readonly ScreenRect Work = new(0, 0, 1200, 800);
    private static GlassCaptureSnapshot Owned(params GlassCaptureWindow[] extra) => new(Crop, Work, 1, 2, 3,
        [new(2, Crop, true, true), new(3, Crop, true, true), .. extra, new(1, Work, true, true)], true,
        new(true, true, "WinSta0", "Default", "Default", true, 1, 1, 0, 42, 1),
        new(368 * 414, 368 * 414, true));

    [Fact]
    public void Wrong_input_desktop_inactive_session_or_unavailable_identity_refuses_before_screen_read()
    {
        var scene = Owned();
        foreach (var desktop in new[] {
            scene.Desktop! with { ThreadDesktop = "Default", InputDesktop = "Winlogon" },
            scene.Desktop! with { ThreadIsInputDesktop = false },
            scene.Desktop! with { QueriesSucceeded = false },
            scene.Desktop! with { UserInteractive = false },
            scene.Desktop! with { ProcessSession = 0 },
            scene.Desktop! with { ActiveConsoleSession = 2 },
            scene.Desktop! with { WtsConnectionState = 4 },
            scene.Desktop! with { Foreground = 0 },
            scene.Desktop! with { ForegroundSession = 2 } })
            Assert.False(GlassCaptureOwnership.Check(scene with { Desktop = desktop }).Allowed);
    }

    [Fact]
    public void Visual_z_order_alone_cannot_override_actual_unowned_input_pixels()
    {
        var scene = Owned();
        Assert.False(GlassCaptureOwnership.Check(scene with { Input = new(1, 368 * 414, false, 100, 100, 66058, "UnownedInputRoot") }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { Input = null }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { Input = new(10, 368 * 414, true) }).Allowed);
    }

    [Fact]
    public void Entire_crop_inside_visible_owned_background_and_work_area_is_allowed()
        => Assert.True(GlassCaptureOwnership.Check(Owned()).Allowed);

    [Fact]
    public void Padded_optical_crop_requires_exact_surface_coverage_and_every_padding_pixel_owned()
    {
        var scene = PaddedOwned();
        Assert.True(GlassCaptureOwnership.Check(scene).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { SurfaceCrop = null }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { SurfaceCrop = new(0, 0, 368, 414) }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { SurfaceCrop = new(90, 90, 400, 450) }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { Input = Owned().Input }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { WorkArea = Crop }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with
        {
            Windows = [new(2, Crop, true, true), new(3, Crop, true, true), new(1, Crop, true, true)]
        }).Allowed);
    }

    [Fact]
    public void Foreign_occluder_in_padding_is_refused_even_when_the_surface_is_clear()
    {
        var scene = PaddedOwned();
        var intruder = new GlassCaptureWindow(99, new(20, 20, 8, 8), true, true);
        Assert.False(GlassCaptureOwnership.Check(scene with { Windows = [intruder, .. scene.Windows] }).Allowed);
    }

    private static GlassCaptureSnapshot PaddedOwned()
    {
        var expanded = new ScreenRect(Crop.Left - 82, Crop.Top - 82, Crop.Width + 164, Crop.Height + 164);
        long pixels = (long)(expanded.Width * expanded.Height);
        return Owned() with { Crop = expanded, SurfaceCrop = Crop, Input = new(pixels, pixels, true) };
    }

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

    [Fact]
    public void Real_owned_drag_target_requires_identity_and_full_crop_coverage_while_foreign_windows_stay_refused()
    {
        var scene = Owned() with
        {
            DragCapture = 4,
            DragCaptureIdentity = new(4, 4, "HwndWrapper", (uint)Environment.ProcessId, "owned-test", 1, 1, true, Work, 0, 0, "Default"),
            Windows = [new(4, Work, true, true), .. Owned().Windows]
        };
        Assert.True(GlassCaptureOwnership.Check(scene).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { DragCaptureIdentity = null }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { DragCaptureIdentity = scene.DragCaptureIdentity with { ProcessId = 0 } }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { DragCaptureIdentity = scene.DragCaptureIdentity with { Bounds = new(0, 0, 1, 1) } }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { Windows = [new(99, Crop, true, true), .. scene.Windows] }).Allowed);
        Assert.False(GlassCaptureOwnership.Check(scene with { Windows = Owned().Windows }).Allowed);
    }
}
