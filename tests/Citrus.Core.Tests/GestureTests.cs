using Citrus.Core;
namespace Citrus.Core.Tests;

public class GestureTests
{
    [Fact]
    public void Queued_activation_is_invalid_after_release_or_explicit_cancellation()
    {
        var gesture = new GestureState(4, 4);
        gesture.ButtonDown(0, 0);
        gesture.Move(10, 0);
        Assert.Equal(OperationMode.Convert, gesture.KeyDown(ModeKey.Shift, 1));
        long queuedEpoch = gesture.Epoch;
        // ButtonUp is also the invalidation path for disable, Escape and disposal.
        gesture.ButtonUp();
        Assert.NotEqual(queuedEpoch, gesture.Epoch);
    }

    [Fact]
    public void Queued_activation_from_old_drag_is_invalid_after_new_button_down()
    {
        var gesture = new GestureState(4, 4);
        gesture.KeyDown(ModeKey.Control, 1);
        gesture.ButtonDown(0, 0);
        Assert.Equal(OperationMode.Actions, gesture.Move(10, 0));
        long queuedEpoch = gesture.Epoch;
        gesture.ButtonDown(100, 100);
        Assert.NotEqual(queuedEpoch, gesture.Epoch);
        Assert.Equal(OperationMode.Actions, gesture.Move(110, 100));
    }

    [Fact]
    public void Modifier_release_and_mode_switch_keep_the_same_drag_epoch()
    {
        var gesture = new GestureState(4, 4);
        gesture.ButtonDown(0, 0);
        gesture.Move(10, 0);
        long epoch = gesture.Epoch;
        gesture.KeyDown(ModeKey.Shift, 1);
        gesture.KeyUp(ModeKey.Shift);
        gesture.KeyDown(ModeKey.Control, 2);
        Assert.Equal(epoch, gesture.Epoch);
    }

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
        Assert.Equal(-1, WheelGeometry.HitTest(62, 184, 2));
    }

    [Theory]
    [InlineData(900, 400, 1)]
    [InlineData(300, 600, 1)]
    [InlineData(800, 600, 2)]
    [InlineData(1920, 972, 2.25)]
    public void Small_work_area_fits_the_whole_presentation_uniformly(double width, double height, double scale)
    {
        var work = new ScreenRect(-width, -height, width, height);
        var bounds = WheelGeometry.Place(-1, -1, work, scale);
        Assert.InRange(bounds.Width, 1, width);
        Assert.InRange(bounds.Height, 1, height);
        Assert.Equal(WheelGeometry.Width / WheelGeometry.Height, bounds.Width / bounds.Height, 8);
        Assert.InRange(bounds.Left, work.Left, work.Right - bounds.Width);
        Assert.InRange(bounds.Top, work.Top, work.Bottom - bounds.Height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(110, 70)]
    [InlineData(-1920, -972)]
    public void Fitted_225_percent_height_roundoff_never_inverts_clamp(double left, double top)
    {
        var work = new ScreenRect(left, top, 1920, 972);
        var bounds = WheelGeometry.Place(left + 900, top + 400, work, 2.25);
        Assert.Equal(972, bounds.Height);
        Assert.Equal(top, bounds.Top);
        Assert.InRange(bounds.Right, work.Left, work.Right);
    }

    [Fact]
    public void Page_hover_waits_400ms_and_navigation_never_selects_an_operation()
    {
        var pages = new WheelPaging();
        Assert.Equal(0, pages.UpdateHover(1, 0, 19));
        Assert.Equal(0, pages.UpdateHover(1, 399, 19));
        Assert.Equal(1, pages.UpdateHover(1, 400, 19));
        Assert.Equal(1, pages.UpdateHover(1, 799, 19));
        Assert.Equal(2, pages.UpdateHover(1, 800, 19));
        Assert.Equal(2, pages.UpdateHover(1, 1200, 19));
        Assert.Equal(2, pages.UpdateHover(-1, 1300, 19));
        Assert.Equal(1, pages.UpdateHover(-1, 1700, 19));
    }

    [Fact]
    public void Scroll_pages_accumulate_partial_notches_clamp_and_reset_hover_dwell()
    {
        var paging = new WheelPaging();
        var scroll = typeof(WheelPaging).GetMethod("Scroll"); Assert.NotNull(scroll);
        int Scroll(int delta,int count=3) => (int)scroll.Invoke(paging,[delta,count])!;
        Assert.Equal(0,Scroll(-60));
        Assert.Equal(1,Scroll(-60));
        Assert.Equal(2,Scroll(-1200));
        Assert.Equal(1,Scroll(120));
        Assert.Equal(0,Scroll(1200));
        Assert.Equal(0,Scroll(-60));
        Assert.Equal(0,Scroll(60));
        Assert.Equal(0,Scroll(-60));
        Assert.Equal(1,Scroll(-60));
        paging.UpdateHover(1,0,24);
        Assert.Equal(2,Scroll(-120));
        Assert.Equal(2,paging.UpdateHover(-1,399,24));
        Assert.Equal(2,paging.UpdateHover(-1,400,24));
        Assert.Equal(1,paging.UpdateHover(-1,799,24));
    }

    [Theory]
    [InlineData(FileAttributes.Offline)]
    [InlineData((FileAttributes)0x40000)]
    [InlineData((FileAttributes)0x400000)]
    public void Cloud_offline_or_recall_attributes_are_rejected_without_opening(FileAttributes attributes) =>
        Assert.False(DropValidation.AttributesAllowed(attributes, OperationMode.Convert));

    [Theory]
    [InlineData(OperationMode.Convert)]
    [InlineData(OperationMode.Tools)]
    [InlineData(OperationMode.Actions)]
    public void Ready_reparse_files_follow_shared_availability_and_directory_policy(OperationMode mode)
    {
        const FileAttributes ready = FileAttributes.ReparsePoint | FileAttributes.Archive;
        Assert.True(DropValidation.AttributesAllowed(ready, mode));
        foreach (var unavailable in new[] { FileAttributes.Offline, (FileAttributes)0x40000, (FileAttributes)0x400000 })
            Assert.False(DropValidation.AttributesAllowed(ready | unavailable, mode));
        Assert.Equal(mode == OperationMode.Actions, DropValidation.AttributesAllowed(ready | FileAttributes.Directory, mode));
    }

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
