using System.Diagnostics;
using System.Runtime.CompilerServices;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class SpringAnimatorTests(DesktopFixture desktop)
{
    [Fact]
    public Task One_rendering_subscription_for_many_channels_then_zero_when_settled() => desktop.RunAsync(() =>
    {
        long now = 0;
        using var animator = new SpringAnimator(() => now, () => true);
        var owners = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
        var values = new double[owners.Length];
        for (int i = 0; i < owners.Length; i++)
        {
            int index = i;
            animator.SetImmediate(owners[i], "opacity", 0, value => values[index] = Math.Clamp(value, 0, 1));
            animator.Retarget(owners[i], "opacity", 1, SpringParameters.Default, value => values[index] = Math.Clamp(value, 0, 1));
        }
        Assert.Equal(32, animator.ActiveChannelCount);
        Assert.True(animator.IsRenderingSubscribed);
        Assert.Equal(1, animator.RenderingSubscriptionCount);
        for (int i = 1; i <= 100; i++) animator.AdvanceAt(now = At(i * .016));
        Assert.All(values, value => Assert.Equal(1, value));
        Assert.Equal(0, animator.ActiveChannelCount);
        Assert.False(animator.IsRenderingSubscribed);
        // Retained state permits another response without reinitialization.
        animator.Retarget(owners[0], "opacity", 0, SpringParameters.Default, value => values[0] = value);
        Assert.Equal(1, values[0]);
        animator.AdvanceAt(now += At(.03));
        Assert.InRange(values[0], 0, 1);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Rapid_press_release_repress_keeps_presentation_continuous() => desktop.RunAsync(() =>
    {
        long now = 0;
        using var animator = new SpringAnimator(() => now, () => true);
        var owner = new object();
        double value = 1;
        animator.SetImmediate(owner, "scale", 1, x => value = x);
        animator.Retarget(owner, "scale", .985, SpringParameters.Press, x => value = x);
        animator.AdvanceAt(now = At(.02));
        var before = animator.GetState(owner, "scale");
        now = At(.025); // Input arrives between rendering frames.
        var expected = CriticalSpring.Advance(before, SpringParameters.Press, .005);
        animator.Retarget(owner, "scale", 1, SpringParameters.Press, x => value = x);
        Assert.Equal(expected.Value, value, 12);
        Assert.Equal(expected.Velocity, animator.GetState(owner, "scale").Velocity, 12);
        animator.Retarget(owner, "scale", .985, SpringParameters.Press, x => value = x);
        Assert.Equal(expected.Value, value, 12);
        Assert.Equal(expected.Velocity, animator.GetState(owner, "scale").Velocity, 12);
        animator.AdvanceAt(At(.01)); // A stale timestamp must not move presentation backwards.
        Assert.Equal(expected.Value, value, 12);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Immediate_cancel_policy_and_dispose_have_exact_callback_contracts() => desktop.RunAsync(() =>
    {
        long now = 0;
        using var animator = new SpringAnimator(() => now, () => true);
        object owner = new();
        int calls = 0;
        double value = -1;
        void Apply(double x) { calls++; value = x; }
        Assert.Throws<InvalidOperationException>(() => animator.Retarget(owner, "a", 1, SpringParameters.Default, Apply));
        animator.SetImmediate(owner, "a", 0, Apply);
        Assert.Equal(0, value);
        animator.Retarget(owner, "a", 1, SpringParameters.Default, Apply);
        animator.AdvanceAt(now = At(.03));
        animator.SetImmediate(owner, "a", .5, Apply);
        Assert.Equal(new SpringState(.5, 0, .5), animator.GetState(owner, "a"));
        Assert.False(animator.IsRenderingSubscribed);
        animator.Retarget(owner, "a", 1, SpringParameters.Default, Apply);
        animator.CancelOwner(owner, true);
        Assert.Equal(1, value);
        Assert.Equal(0, animator.RetainedChannelCount);
        animator.SetImmediate(owner, "a", 0, Apply);
        animator.Retarget(owner, "a", 1, SpringParameters.Default, Apply);
        int before = calls;
        animator.CancelOwner(owner, false);
        animator.AdvanceAt(now += At(.02));
        Assert.Equal(before, calls);
        animator.SetImmediate(owner, "a", 0, Apply);
        animator.Retarget(owner, "a", 1, SpringParameters.Default, Apply);
        animator.MotionEnabled = false;
        Assert.Equal(1, value);
        Assert.Equal(0, animator.RetainedChannelCount);
        Assert.False(animator.IsRenderingSubscribed);
        animator.SetImmediate(owner, "a", 0, Apply);
        before = calls;
        animator.Dispose();
        Assert.Equal(before, calls);
        Assert.Throws<ObjectDisposedException>(() => animator.SetImmediate(owner, "a", 1, Apply));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Long_gap_applies_exact_target_and_does_not_schedule_idle_frames() => desktop.RunAsync(() =>
    {
        long now = 0;
        using var animator = new SpringAnimator(() => now, () => true);
        object owner = new();
        int calls = 0;
        double value = 0;
        void Apply(double x) { calls++; value = x; }
        animator.SetImmediate(owner, "a", 0, Apply);
        animator.Retarget(owner, "a", 1, SpringParameters.Default, Apply);
        animator.AdvanceAt(now = At(.251));
        Assert.Equal(1, value);
        Assert.Equal(new SpringState(1, 0, 1), animator.GetState(owner, "a"));
        Assert.False(animator.IsRenderingSubscribed);
        int settledCalls = calls;
        animator.AdvanceAt(now += At(.1));
        Assert.Equal(settledCalls, calls);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Abandoned_owner_can_collect_and_last_rendering_subscription_is_removed() => desktop.RunAsync(() =>
    {
        long now = 0;
        using var animator = new SpringAnimator(() => now, () => true);
        var abandoned = StartAbandonedOwner(animator);
        Assert.True(animator.IsRenderingSubscribed);
        for (int i = 0; i < 3 && abandoned.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(abandoned.IsAlive);
        animator.AdvanceAt(now = At(.01));
        Assert.Equal(0, animator.ActiveChannelCount);
        Assert.Equal(0, animator.RetainedChannelCount);
        Assert.False(animator.IsRenderingSubscribed);
        return Task.CompletedTask;
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StartAbandonedOwner(SpringAnimator animator)
    {
        var owner = new object();
        // The callback deliberately captures the owner: ephemeron storage must still permit collection.
        Action<double> apply = _ => GC.KeepAlive(owner);
        animator.SetImmediate(owner, "a", 0, apply);
        animator.Retarget(owner, "a", 1, SpringParameters.Default, apply);
        return new WeakReference(owner);
    }

    private static long At(double seconds) => (long)Math.Round(seconds * Stopwatch.Frequency);
}
