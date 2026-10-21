using Citrus.Core;

namespace Citrus.Core.Tests;

public sealed class ImmediateModifierActivationTests
{
    [Theory]
    [InlineData(ModeKey.Shift, OperationMode.Convert, 0)]
    [InlineData(ModeKey.Shift, OperationMode.Convert, 121)]
    [InlineData(ModeKey.Shift, OperationMode.Convert, 600000)]
    [InlineData(ModeKey.Control, OperationMode.Actions, 0)]
    [InlineData(ModeKey.Control, OperationMode.Actions, 121)]
    [InlineData(ModeKey.Control, OperationMode.Actions, 600000)]
    [InlineData(ModeKey.Alt, OperationMode.Tools, 0)]
    [InlineData(ModeKey.Alt, OperationMode.Tools, 121)]
    [InlineData(ModeKey.Alt, OperationMode.Tools, 600000)]
    public void Preheld_modifier_activates_on_threshold_regardless_of_key_age(ModeKey key, OperationMode expected, int age)
    {
        var gesture = new GestureState(4, 4);
        gesture.KeyDown(key, 1000);
        gesture.ButtonDown(10, 10);
        Assert.Null(gesture.Move(13, 13, 1000 + age));
        Assert.Equal(expected, gesture.Move(14, 10, 1000 + age));
        Assert.Equal(expected, gesture.LatchedMode);
        Assert.Null(gesture.Move(50, 10, 1001 + age));
        gesture.KeyUp(key);
        Assert.Equal(expected, gesture.LatchedMode);
    }

    [Theory]
    [InlineData(0xA0u, OperationMode.Convert)]
    [InlineData(0xA1u, OperationMode.Convert)]
    [InlineData(0xA2u, OperationMode.Actions)]
    [InlineData(0xA3u, OperationMode.Actions)]
    [InlineData(0xA4u, OperationMode.Tools)]
    [InlineData(0xA5u, OperationMode.Tools)]
    public void Startup_snapshot_key_also_activates_at_threshold(uint key, OperationMode expected)
    {
        var gesture = new GestureState(4, 4);
        var modifiers = new ModifierObserver(gesture);
        modifiers.Seed(key);
        Assert.Null(modifiers.Observe(key, true, 1000)); // Repeated down is still not a new press.
        gesture.ButtonDown(0, 0);
        Assert.Equal(expected, gesture.Move(4, 0, 10000));
    }

    [Fact]
    public void Slow_movement_before_threshold_does_not_add_a_hold_delay()
    {
        var gesture = new GestureState(4, 4);
        gesture.KeyDown(ModeKey.Shift, 1000);
        gesture.ButtonDown(10, 10);
        Assert.Null(gesture.Move(13, 13, 5000));
        Assert.Equal(OperationMode.Convert, gesture.Move(14, 10, 50000));
    }

    [Theory]
    [InlineData(ModeKey.Shift)]
    [InlineData(ModeKey.Control)]
    [InlineData(ModeKey.Alt)]
    public void Modifier_clicks_without_drag_never_activate_the_wheel(ModeKey key)
    {
        var gesture = new GestureState(4, 4);
        gesture.KeyDown(key, 1);
        for (int i = 0; i < 3; i++)
        {
            gesture.ButtonDown(10, 10);
            Assert.Null(gesture.Move(13, 13, 10000 + i));
            gesture.ButtonUp();
            Assert.Null(gesture.Move(100, 100, 20000 + i));
            Assert.Null(gesture.LatchedMode);
        }
    }

    [Fact]
    public void Releasing_one_physical_control_keeps_the_other_active()
    {
        var gesture = new GestureState(4, 4);
        var modifiers = new ModifierObserver(gesture);
        modifiers.Seed(0xA2); modifiers.Seed(0xA3);
        modifiers.Observe(0xA2, false, 1000);
        gesture.ButtonDown(0, 0);
        Assert.Equal(OperationMode.Actions, gesture.Move(4, 0, 10000));
        modifiers.Observe(0xA3, false, 10001);
        Assert.Equal(OperationMode.Actions, gesture.LatchedMode);
        gesture.ButtonDown(0, 0);
        Assert.Null(gesture.Move(4, 0, 10002));
    }

    [Fact]
    public void Releasing_key_before_drag_prevents_activation_and_fresh_drag_key_still_latches()
    {
        var gesture = new GestureState(4, 4);
        gesture.KeyDown(ModeKey.Control, 1);
        gesture.ButtonDown(0, 0);
        gesture.KeyUp(ModeKey.Control);
        Assert.Null(gesture.Move(4, 0, 10000));
        Assert.Equal(OperationMode.Tools, gesture.KeyDown(ModeKey.Alt, 10001));
        Assert.Null(gesture.KeyDown(ModeKey.Alt, 10002));
        gesture.KeyUp(ModeKey.Alt);
        Assert.Equal(OperationMode.Tools, gesture.LatchedMode);
    }
}
