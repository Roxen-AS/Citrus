using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Citrus.Windows.Presentation;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class PressFeedbackTests(DesktopFixture desktop)
{
    [Fact]
    public Task Resources_without_App_Startup_use_safe_native_press_feedback() => desktop.RunAsync(() =>
    {
        using var button = MakeButton();
        Assert.Null(PressFeedback.GetAnimator(button));
        button.Press();
        Assert.True(button.IsPressed);
        Assert.Equal(.12, Part<Border>(button, "PressedTint").Opacity);
        Assert.Equal(.55, Part<Border>(button, "PressedEdge").Opacity);
        Assert.Equal(1, Scale(button).ScaleX);
        button.Release();
        Assert.False(button.IsPressed);
        Assert.Equal(0, Part<Border>(button, "PressedTint").Opacity);
        return Task.CompletedTask;
    });

    [Fact]
    public Task IsPressed_feedback_precedes_click_and_dragaway_cancels() => desktop.RunAsync(async () =>
    {
        using var button = MakeButton();
        int clicks = 0;
        button.Click += (_, _) => { Assert.False(button.IsPressed); clicks++; };
        button.Press();
        Assert.Equal(.12, Part<Border>(button, "PressedTint").Opacity);
        Assert.Equal(0, clicks);
        button.Release();
        Assert.Equal(1, clicks);

        // Exercise real WPF pointer capture and drag-away routing in the owned native window.
        Assert.True(GetCursorPos(out var original));
        try
        {
            button.Host.Topmost = true;
            button.Host.UpdateLayout();
            var center = button.PointToScreen(new System.Windows.Point(90, 24));
            Assert.True(SetCursorPos((int)center.X, (int)center.Y));
            await Task.Delay(30);
            MouseEvent(0x0002, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTDOWN
            await WaitFor(() => button.IsPressed);
            Assert.Equal(.12, Part<Border>(button, "PressedTint").Opacity);
            Assert.Equal(1, clicks);
            var outside = button.PointToScreen(new System.Windows.Point(190, 24));
            Assert.True(SetCursorPos((int)outside.X, (int)outside.Y));
            await WaitFor(() => !button.IsPressed);
            Assert.Equal(0, Part<Border>(button, "PressedTint").Opacity);
            MouseEvent(0x0004, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTUP
            await Task.Delay(30);
            Assert.Equal(1, clicks);
        }
        finally
        {
            MouseEvent(0x0004, 0, 0, 0, UIntPtr.Zero);
            SetCursorPos(original.X, original.Y);
        }
    });

    [NativeAnimationFact]
    public Task Unloaded_owner_and_reduced_motion_remove_channels() => desktop.RunAsync(() =>
    {
        long now = 0;
        using var animator = new SpringAnimator(() => now, () => true);
        using var button = MakeButton();
        PressFeedback.SetMotionEnabled(button, true);
        PressFeedback.SetAnimator(button, animator);
        button.Press();
        Assert.Equal(1, animator.ActiveChannelCount);
        animator.AdvanceAt(now = At(.02));
        Assert.True(Scale(button).ScaleX < 1);
        Assert.Equal(180, button.ActualWidth);
        Assert.Equal(48, button.ActualHeight);
        Assert.True(button.RenderTransform.Value.IsIdentity);
        Assert.NotNull(button.FocusVisualStyle);
        PressFeedback.SetMotionEnabled(button, false);
        Assert.Equal(1, Scale(button).ScaleX);
        Assert.Equal(0, animator.RetainedChannelCount);
        Assert.False(animator.IsRenderingSubscribed);
        Assert.Equal(.12, Part<Border>(button, "PressedTint").Opacity);
        button.Release();
        PressFeedback.SetMotionEnabled(button, true);
        button.Press();
        Assert.True(animator.IsRenderingSubscribed);
        button.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, button));
        Assert.Equal(0, animator.RetainedChannelCount);
        Assert.False(animator.IsRenderingSubscribed);
        Assert.Equal(1, Scale(button).ScaleX);
        return Task.CompletedTask;
    });

    [Fact]
    public Task No_shared_global_brush_is_mutated() => desktop.RunAsync(() =>
    {
        using var first = MakeButton();
        using var second = MakeButton();
        var shared = first.Background;
        Assert.Same(shared, second.Background);
        double opacity = shared.Opacity;
        first.Press();
        Assert.Equal(opacity, shared.Opacity);
        Assert.Equal(0, Part<Border>(second, "PressedTint").Opacity);
        Assert.Equal(.12, Part<Border>(first, "PressedTint").Opacity);
        first.Release();
        return Task.CompletedTask;
    });

    [NativeAnimationFact]
    public Task Inherited_scheduler_replacement_cancels_old_owner() => desktop.RunAsync(() =>
    {
        long now = 0;
        using var oldAnimator = new SpringAnimator(() => now, () => true);
        using var newAnimator = new SpringAnimator(() => now, () => true);
        var root = new Grid();
        using var button = MakeButton(root);
        PressFeedback.SetMotionEnabled(button, true);
        PressFeedback.SetAnimator(root, oldAnimator);
        Assert.Same(oldAnimator, PressFeedback.GetAnimator(button));
        button.Press();
        oldAnimator.AdvanceAt(now = At(.02));
        Assert.True(Scale(button).ScaleX < 1);
        PressFeedback.SetAnimator(root, newAnimator);
        Assert.Equal(0, oldAnimator.RetainedChannelCount);
        Assert.False(oldAnimator.IsRenderingSubscribed);
        Assert.Equal(1, Scale(button).ScaleX);
        button.Release();
        button.Press();
        Assert.True(newAnimator.IsRenderingSubscribed);
        button.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, button));
        Assert.False(newAnimator.IsRenderingSubscribed);
        return Task.CompletedTask;
    });

    private static TestButton MakeButton(Grid? root = null)
    {
        var button = new TestButton { Content = "Press", Width = 180, Height = 48,
            Style = (Style)System.Windows.Application.Current.FindResource("CitrusButton") };
        if (root is not null) root.Children.Add(button);
        button.Host = new Window { Content = (object?)root ?? button, Width = 220, Height = 100,
            Left = 100, Top = 100, ShowActivated = true, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        button.Host.Show();
        button.ApplyTemplate();
        button.Measure(new System.Windows.Size(180, 48));
        button.Arrange(new Rect(0, 0, 180, 48));
        return button;
    }
    private static T Part<T>(Button button, string name) where T : class => Assert.IsType<T>(button.Template.FindName(name, button));
    private static ScaleTransform Scale(Button button) => Assert.IsType<ScaleTransform>(Part<ContentPresenter>(button, "PressContent").RenderTransform);
    private static long At(double seconds) => (long)Math.Round(seconds * Stopwatch.Frequency);

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "Native pointer event did not reach the owned WPF button.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private sealed class TestButton : Button, IDisposable
    {
        public Window Host { get; set; } = null!;
        public void Dispose() => Host.Close();
        public void Press() => OnKeyDown(KeyArgs(Keyboard.KeyDownEvent));
        public void Release() => OnKeyUp(KeyArgs(Keyboard.KeyUpEvent));
        private KeyEventArgs KeyArgs(RoutedEvent routedEvent) => new(Keyboard.PrimaryDevice,
            new TestSource(), Environment.TickCount, Key.Space) { RoutedEvent = routedEvent, Source = this };

    }
    private sealed class TestSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
