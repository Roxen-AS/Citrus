using Citrus.Core;
namespace Citrus.Core.Tests;

public class GestureTests
{
    [Fact]
    public void Preheld_key_activates_only_after_drag_threshold()
    {
        var gesture = new GestureState(4, 4);
        Assert.Null(gesture.KeyDown(ModeKey.Shift, 1));
        gesture.ButtonDown(100, 100);
        Assert.Null(gesture.Move(102, 102));
        Assert.Equal(OperationMode.Convert, gesture.Move(105, 100));
        Assert.Null(gesture.Move(106, 100));
    }

    [Fact]
    public void Tapped_mode_latches_repeats_are_ignored_and_latest_key_wins()
    {
        var gesture = new GestureState(4, 4);
        gesture.ButtonDown(0, 0);
        Assert.Null(gesture.Move(10, 0));
        Assert.Equal(OperationMode.Convert, gesture.KeyDown(ModeKey.Shift, 2));
        Assert.Null(gesture.KeyDown(ModeKey.Shift, 3));
        gesture.KeyUp(ModeKey.Shift);
        Assert.Equal(OperationMode.Convert, gesture.LatchedMode);
        Assert.Equal(OperationMode.Tools, gesture.KeyDown(ModeKey.Alt, 4));
        Assert.Equal(OperationMode.Actions, gesture.KeyDown(ModeKey.Control, 5));
        gesture.ButtonUp();
        Assert.Null(gesture.LatchedMode);
    }

    [Fact]
    public void Simultaneous_keys_prefer_control_then_alt_then_shift()
    {
        var gesture = new GestureState(4, 4);
        gesture.KeyDown(ModeKey.Control, 9);
        gesture.KeyDown(ModeKey.Alt, 9);
        gesture.KeyDown(ModeKey.Shift, 9);
        gesture.ButtonDown(0, 0);
        Assert.Equal(OperationMode.Actions, gesture.Move(8, 0));
    }

    [Fact]
    public void Confirmation_timeout_is_750ms_and_old_sessions_cannot_close_new_ones()
    {
        var session = new WheelSession();
        long first = session.Begin(100, 10, 10);
        Assert.False(session.ShouldExpire(first, 849));
        Assert.True(session.ShouldExpire(first, 850));
        long second = session.Begin(200, 10, 10);
        Assert.False(session.ShouldExpire(first, 2000));
        session.ConfirmPayload();
        Assert.False(session.ShouldExpire(second, 2000));
    }

    [Fact]
    public void Clamped_cursor_starts_unselected_until_deliberate_movement()
    {
        var session = new WheelSession();
        session.Begin(0, 30, 30);
        Assert.False(session.ObservePointer(30, 30));
        Assert.False(session.ObservePointer(33, 32));
        Assert.True(session.ObservePointer(40, 30));
        Assert.True(session.ObservePointer(30, 30));
    }

    [Theory]
    [InlineData(-1920, -200, 1920, 1080, -1920, -200, 1.5)]
    [InlineData(0, 0, 1920, 1040, 1919, 1039, 2)]
    [InlineData(-1280, 0, 1280, 720, -1, 719, 1)]
    public void Wheel_bounds_fit_monitor_in_physical_pixels(double left, double top, double width, double height, double x, double y, double scale)
    {
        var work = new ScreenRect(left, top, width, height);
        var bounds = WheelGeometry.Place(x, y, work, scale);
        Assert.InRange(bounds.Left, work.Left, work.Right - bounds.Width);
        Assert.InRange(bounds.Top, work.Top, work.Bottom - bounds.Height);
        Assert.Equal(WheelGeometry.Width * scale, bounds.Width);
        Assert.Equal(WheelGeometry.Height * scale, bounds.Height);
    }

    [Fact]
    public void Neutral_center_outside_and_empty_sectors_cancel()
    {
        Assert.Equal(-1, WheelGeometry.HitTest(168, 162, 8));
        Assert.Equal(-1, WheelGeometry.HitTest(0, 0, 8));
        Assert.Equal(0, WheelGeometry.HitTest(168, 50, 8));
        Assert.Equal(-1, WheelGeometry.HitTest(56, 162, 2));
    }

    [Fact]
    public void Page_hover_waits_400ms_and_navigation_never_selects_an_operation()
    {
        var pages = new WheelPaging();
        Assert.Equal(0, pages.UpdateHover(1, 0, 18));
        Assert.Equal(0, pages.UpdateHover(1, 399, 18));
        Assert.Equal(1, pages.UpdateHover(1, 400, 18));
        Assert.Equal(1, pages.UpdateHover(1, 799, 18));
        Assert.Equal(2, pages.UpdateHover(1, 800, 18));
        Assert.Equal(2, pages.UpdateHover(1, 1200, 18));
        Assert.Equal(2, pages.UpdateHover(-1, 1300, 18));
        Assert.Equal(1, pages.UpdateHover(-1, 1700, 18));
    }

    [Theory]
    [InlineData(FileAttributes.Offline)]
    [InlineData((FileAttributes)0x40000)]
    [InlineData((FileAttributes)0x400000)]
    public void Cloud_offline_or_recall_attributes_are_rejected_without_opening(FileAttributes attributes) =>
        Assert.False(DropValidation.AttributesAllowed(attributes, OperationMode.Convert));

    [Fact]
    public void Directory_missing_and_non_file_payloads_cannot_convert()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            Assert.False(DropValidation.Accepts([directory], OperationMode.Convert));
            Assert.False(DropValidation.Accepts([directory], OperationMode.Tools));
            Assert.True(DropValidation.Accepts([directory], OperationMode.Actions));
            Assert.False(DropValidation.Accepts([Path.Combine(directory, "missing.png")], OperationMode.Convert));
            Assert.False(DropValidation.Accepts([], OperationMode.Convert));
            Assert.False(DropValidation.Accepts(["https://example.com/image.png"], OperationMode.Convert));
        }
        finally { Directory.Delete(directory); }
    }
}

public class ModifierObserverTests
{
    [Fact]
    public void Opposite_physical_modifier_is_a_new_press_but_repeats_and_partial_release_are_not()
    {
        var gesture = new GestureState(4, 4);
        var modifiers = new ModifierObserver(gesture);
        gesture.ButtonDown(0, 0);
        gesture.Move(10, 0);
        Assert.Equal(OperationMode.Convert, modifiers.Observe(0xA0, true, 1));
        Assert.Equal(OperationMode.Tools, modifiers.Observe(0xA4, true, 2));
        Assert.Equal(OperationMode.Convert, modifiers.Observe(0xA1, true, 3));
        Assert.Null(modifiers.Observe(0xA1, true, 4));
        modifiers.Observe(0xA1, false, 5);
        Assert.Equal(OperationMode.Convert, gesture.LatchedMode);
        gesture.ButtonUp();
        gesture.ButtonDown(0, 0);
        Assert.Equal(OperationMode.Convert, gesture.Move(10, 0));
    }

    [Fact]
    public void Alt_normal_and_system_message_families_observe_both_up_and_down()
    {
        var gesture = new GestureState(4, 4);
        var modifiers = new ModifierObserver(gesture);
        gesture.ButtonDown(0, 0);
        gesture.Move(10, 0);
        Assert.Equal(OperationMode.Tools, modifiers.ObserveMessage(0x104, 0xA4, 1));
        Assert.Null(modifiers.ObserveMessage(0x104, 0xA4, 2));
        modifiers.ObserveMessage(0x105, 0xA4, 3);
        Assert.Equal(OperationMode.Tools, gesture.LatchedMode);
        Assert.Equal(OperationMode.Tools, modifiers.ObserveMessage(0x100, 0xA5, 4));
        modifiers.ObserveMessage(0x101, 0xA5, 5);
        gesture.ButtonUp();
        gesture.ButtonDown(0, 0);
        Assert.Null(gesture.Move(10, 0));
    }
}
