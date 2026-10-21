using Citrus.Core;

namespace Citrus.Core.Tests;

public class WheelSessionTests
{
    [Theory]
    [InlineData("expanded")]
    [InlineData("reordered")]
    [InlineData("shrunk")]
    public void First_payload_is_immutable_across_reconfirmation_and_mode_switches(string change)
    {
        var session = new WheelSession();
        session.RequestMode(0, OperationMode.Actions, 100, 100);
        string[] first = ["A", "B"];
        session.ConfirmPayload(first);
        first[0] = "mutated source";
        string[] later = change switch { "expanded" => ["A", "B", "C"], "reordered" => ["B", "A"], _ => ["A"] };
        session.RequestMode(10, OperationMode.Convert, 200, 200);
        session.ConfirmPayload(later);
        session.RequestMode(20, OperationMode.Actions, 300, 300);
        Assert.Equal(new[] { "A", "B" }, session.Paths);
        Assert.Equal((100d, 100d), session.Anchor);
        Assert.True(session.TryCompleteDrop(session.Generation));
    }

    [Fact]
    public void Mode_switch_preserves_anchor_generation_and_confirmed_payload()
    {
        var session = new WheelSession();
        Assert.True(session.RequestMode(0, OperationMode.Convert, -800, 200));
        long generation = session.Generation;
        session.ResetPointer(168, 162);
        string[] payload = ["/images/source.png"];
        session.ConfirmPayload(payload);
        payload[0] = "/images/changed.png";
        Assert.True(session.ObservePointer(168, 50));

        Assert.False(session.RequestMode(500, OperationMode.Tools, -800, 88));
        session.ResetPointer(168, 50);
        Assert.Equal((-800d, 200d), session.Anchor);
        Assert.Equal(generation, session.Generation);
        Assert.Equal(OperationMode.Tools, session.Mode);
        Assert.Equal("/images/source.png", Assert.Single(session.Paths));
        Assert.False(session.ObservePointer(168, 50));
        Assert.False(session.ShouldExpire(generation, 10000));
        Assert.False(session.GlobalRelease(generation));
        Assert.True(session.ObservePointer(178, 50));
        Assert.True(session.TryCompleteDrop(generation));
    }

    [Fact]
    public void Unconfirmed_mode_switch_does_not_restart_confirmation_deadline()
    {
        var session = new WheelSession();
        session.RequestMode(100, OperationMode.Convert, 100, 100);
        long generation = session.Generation;
        session.RequestMode(800, OperationMode.Tools, 140, 140);
        Assert.Equal(generation, session.Generation);
        Assert.False(session.ShouldExpire(generation, 849));
        Assert.True(session.ShouldExpire(generation, 850));
    }

    [Fact]
    public void Independent_activation_after_cancel_gets_new_anchor_and_empty_payload()
    {
        var session = new WheelSession();
        session.RequestMode(0, OperationMode.Convert, 100, 100);
        long old = session.Generation;
        session.ConfirmPayload(["/images/source.png"]);
        session.Cancel(old);
        Assert.True(session.RequestMode(1000, OperationMode.Tools, -500, -200));
        Assert.NotEqual(old, session.Generation);
        Assert.Equal((-500d, -200d), session.Anchor);
        Assert.Empty(session.Paths);
        Assert.False(session.GlobalRelease(old));
        Assert.False(session.ShouldExpire(session.Generation, 1749));
        Assert.True(session.ShouldExpire(session.Generation, 1750));
    }

    [Fact]
    public void Global_release_before_delayed_ole_drop_keeps_confirmed_target_alive()
    {
        var session = new WheelSession();
        long generation = session.Begin(0, 168, 162);
        session.ConfirmPayload();
        Assert.True(session.ObservePointer(168, 50));
        Assert.False(session.GlobalRelease(generation));
        Assert.False(session.ShouldExpire(generation, 20_000));
        Assert.True(session.TryCompleteDrop(generation));
        Assert.False(session.TryCompleteDrop(generation));
    }

    [Fact]
    public void Global_release_can_close_an_unconfirmed_capture()
    {
        var session = new WheelSession();
        long generation = session.Begin(0, 0, 0);
        Assert.True(session.GlobalRelease(generation));
        Assert.False(session.TryCompleteDrop(generation));
    }

    [Fact]
    public void Ole_leave_or_explicit_cancel_ends_confirmed_ownership()
    {
        var session = new WheelSession();
        long generation = session.Begin(0, 0, 0);
        session.ConfirmPayload();
        Assert.True(session.Cancel(generation));
        Assert.False(session.TryCompleteDrop(generation));
        Assert.False(session.Cancel(generation));
    }

    [Fact]
    public void Stale_release_cancel_drop_and_expiry_cannot_end_a_new_session()
    {
        var session = new WheelSession();
        long old = session.Begin(0, 0, 0);
        session.ConfirmPayload();
        session.Cancel(old);
        long current = session.Begin(100, 0, 0);
        Assert.False(session.GlobalRelease(old));
        Assert.False(session.Cancel(old));
        Assert.False(session.TryCompleteDrop(old));
        Assert.False(session.ShouldExpire(old, 1000));
        session.ConfirmPayload();
        Assert.True(session.TryCompleteDrop(current));
    }
}
