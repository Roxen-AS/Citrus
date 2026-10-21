using Citrus.Core;

namespace Citrus.Windows.Tests;

public sealed class PressInputOwnershipTests
{
    private static GlassWindowIdentity Window(long handle = 10, long root = 10, uint process = 123) =>
        new(handle, root, "HwndWrapper", process, "Citrus.Windows.Tests", 50, 2, true,
            new ScreenRect(100, 100, 220, 100), 0, 0, "Default");

    private static PressInputSnapshot Owned()
    {
        var desktop = new GlassDesktopSnapshot(true, true, "WinSta0", "Default", "Default", true,
            2, 2, 0, 10, 2, ForegroundWindow: Window());
        return new(desktop, desktop, 10, 123, Window(), Window(), true, true, true, true, true, true, null);
    }

    [Fact]
    public void Actual_owned_host_target_and_input_desktop_allow_one_operation()
        => Assert.True(PressInputOwnership.Check(Owned()).Allowed);

    [Fact]
    public void Unowned_foreground_refuses_even_when_topmost_host_and_target_are_owned()
    {
        var scene = Owned();
        var foreign = scene.Before with { Foreground = 99, ForegroundWindow = Window(99, 99, 999) };
        Assert.False(PressInputOwnership.Check(scene with { Before = foreign, After = foreign }).Allowed);
    }

    [Fact]
    public void Unowned_native_hit_or_same_process_sibling_window_refuses()
    {
        var scene = Owned();
        Assert.False(PressInputOwnership.Check(scene with { Hit = Window(99, 99, 999) }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { Hit = Window(99, 99) }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { Hit = Window(process: 999) }).Allowed);
    }

    [Fact]
    public void Child_native_hit_of_actual_owned_host_is_allowed()
        => Assert.True(PressInputOwnership.Check(Owned() with { Hit = Window(11) }).Allowed);

    [Fact]
    public void Invalid_desktop_session_or_query_refuses()
    {
        var scene = Owned();
        foreach (var desktop in new[] {
            scene.Before with { InputDesktop = "Winlogon" },
            scene.Before with { ThreadIsInputDesktop = false },
            scene.Before with { ProcessSession = 0 },
            scene.Before with { ActiveConsoleSession = 3 },
            scene.Before with { QueriesSucceeded = false },
            scene.Before with { ForegroundWindow = null } })
            Assert.False(PressInputOwnership.Check(scene with { Before = desktop, After = desktop }).Allowed);
    }

    [Fact]
    public void Desktop_or_foreground_change_during_preflight_refuses()
    {
        var scene = Owned();
        Assert.False(PressInputOwnership.Check(scene with {
            After = scene.After with { Foreground = 11, ForegroundWindow = Window(11) } }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with {
            After = scene.After with { ThreadDesktopHandle = 42 } }).Allowed);
    }

    [Fact]
    public void Hidden_disabled_or_foreign_host_refuses()
    {
        var scene = Owned();
        Assert.False(PressInputOwnership.Check(scene with { HostVisible = false }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { HostEnabled = false }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { HostHandle = 0 }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { Host = Window(process: 999) }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { Host = Window() with { SessionId = 3 } }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { Host = Window() with { DesktopName = "Other" } }).Allowed);
    }

    [Fact]
    public void Wpf_source_target_or_current_cursor_mismatch_refuses()
    {
        var scene = Owned();
        Assert.False(PressInputOwnership.Check(scene with { WpfSourceMatches = false }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { WpfTargetMatches = false }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { CursorMatches = false }).Allowed);
    }

    [Fact]
    public void Unknown_or_foreign_capture_refuses_owned_capture_allows()
    {
        var scene = Owned();
        Assert.False(PressInputOwnership.Check(scene with { CaptureKnown = false }).Allowed);
        Assert.False(PressInputOwnership.Check(scene with { Capture = Window(99, 99, 999) }).Allowed);
        Assert.True(PressInputOwnership.Check(scene with { Capture = Window() }).Allowed);
    }
}
