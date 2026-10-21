using Citrus.Core.Presentation;

namespace Citrus.Core.Tests;

public sealed class CriticalSpringTests
{
    private static readonly SpringParameters Parameters = new(.34, .001, .01);

    [Fact]
    public void Analytic_step_matches_partitioned_elapsed_time()
    {
        var initial = new SpringState(.2, -2, 1);
        var whole = CriticalSpring.Advance(initial, Parameters, .1);
        var partitioned = initial;
        for (int i = 0; i < 10; i++) partitioned = CriticalSpring.Advance(partitioned, Parameters, .01);
        Assert.Equal(whole.Value, partitioned.Value, 12);
        Assert.Equal(whole.Velocity, partitioned.Velocity, 12);
        double omega = 2 * Math.PI / .34;
        double c = -2 + omega * (.2 - 1);
        Assert.Equal(1 + (-.8 + c * .1) * Math.Exp(-omega * .1), whole.Value, 12);
    }

    [Fact]
    public void Retarget_preserves_current_value_and_velocity()
    {
        var moving = CriticalSpring.Advance(new(0, 0, 1), Parameters, .04);
        var reversed = CriticalSpring.Retarget(moving, 0);
        Assert.Equal(moving.Value, reversed.Value);
        Assert.Equal(moving.Velocity, reversed.Velocity);
        Assert.Equal(0, reversed.Target);
        Assert.True(CriticalSpring.Advance(reversed, Parameters, .001).Value > reversed.Value);
    }

    [Fact]
    public void Response_is_not_fixed_duration()
    {
        var state = CriticalSpring.Advance(new(0, 0, 1), Parameters, .17);
        state = CriticalSpring.Advance(state, Parameters, .17);
        Assert.False(CriticalSpring.IsSettled(state, Parameters));
        Assert.InRange(state.Value, .98, .999);
        var departing = CriticalSpring.Advance(new(1, 3, 1), Parameters, .01);
        Assert.True(departing.Value > 1);
        Assert.False(CriticalSpring.IsSettled(departing, Parameters));
    }

    [Fact]
    public void Finite_validation_and_settle_thresholds()
    {
        Assert.True(CriticalSpring.IsSettled(new(.9995, .009, 1), Parameters));
        Assert.False(CriticalSpring.IsSettled(new(.9995, .011, 1), Parameters));
        Assert.False(CriticalSpring.IsSettled(new(.998, 0, 1), Parameters));
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CriticalSpring.Advance(new(invalid, 0, 1), Parameters, .01));
            Assert.Throws<ArgumentOutOfRangeException>(() => CriticalSpring.Advance(new(0, invalid, 1), Parameters, .01));
            Assert.Throws<ArgumentOutOfRangeException>(() => CriticalSpring.Retarget(new(0, 0, 1), invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => CriticalSpring.Advance(new(0, 0, 1), Parameters, invalid));
        }
        foreach (double response in new[] { 0, .049, 2.01, double.NaN })
            Assert.Throws<ArgumentOutOfRangeException>(() => CriticalSpring.Advance(new(0, 0, 1), new(response, .001, .01), .01));
        Assert.Throws<ArgumentOutOfRangeException>(() => CriticalSpring.Advance(new(0, 0, 1), Parameters, -.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => CriticalSpring.IsSettled(new(0, 0, 1), new(.34, 0, .01)));
    }

    [Fact]
    public void Long_frame_gap_settles_without_catchup()
    {
        Assert.Equal(new SpringState(1, 0, 1), CriticalSpring.Advance(new(0, 20, 1), Parameters, .251));
        Assert.NotEqual(1, CriticalSpring.Advance(new(0, 0, 1), Parameters, .25).Value);
        var state = new SpringState(0, 0, 1);
        for (int i = 0; i < 200 && !CriticalSpring.IsSettled(state, Parameters); i++)
            state = CriticalSpring.Advance(state, Parameters, .01);
        Assert.Equal(new SpringState(1, 0, 1), state);
    }
}
