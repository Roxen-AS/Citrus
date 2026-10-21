using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Citrus.Core.Presentation;

namespace Citrus.Windows.Presentation;

/// <summary>Optional content-only response, driven by native ButtonBase.IsPressed. Click/capture/focus stay native.</summary>
public static class PressFeedback
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty AnimatorProperty = DependencyProperty.RegisterAttached(
        "Animator", typeof(SpringAnimator), typeof(PressFeedback),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnAnimatorChanged));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.RegisterAttached(
        "MotionEnabled", typeof(bool), typeof(PressFeedback), new PropertyMetadata(true, OnMotionChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(FeedbackState), typeof(PressFeedback));
    private static readonly DependencyProperty ObservedPressedProperty = DependencyProperty.RegisterAttached(
        "ObservedPressed", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false, OnPressedChanged));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static SpringAnimator? GetAnimator(DependencyObject target) => (SpringAnimator?)target.GetValue(AnimatorProperty);
    public static void SetAnimator(DependencyObject target, SpringAnimator? value) => target.SetValue(AnimatorProperty, value);
    public static bool GetMotionEnabled(DependencyObject target) => (bool)target.GetValue(MotionEnabledProperty);
    public static void SetMotionEnabled(DependencyObject target, bool value) => target.SetValue(MotionEnabledProperty, value);

    private static FeedbackState? State(DependencyObject target) => (FeedbackState?)target.GetValue(StateProperty);
    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ButtonBase button) return;
        if ((bool)args.NewValue)
        {
            if (State(button) is not null) return;
            var state = new FeedbackState(button);
            button.SetValue(StateProperty, state);
            state.Attach();
        }
        else
        {
            State(button)?.Detach();
            button.ClearValue(StateProperty);
        }
    }
    private static void OnAnimatorChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => State(target)?.Reset();
    private static void OnMotionChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => State(target)?.Reset();
    private static void OnPressedChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => State(target)?.Update();

    private sealed class FeedbackState(ButtonBase button)
    {
        private SpringAnimator? animator;
        private ScaleTransform? scale;
        private bool initialized;
        private bool suspended;

        public void Attach()
        {
            button.Loaded += OnLoaded;
            button.Unloaded += OnUnloaded;
            ObservePressed();
        }
        public void Detach()
        {
            suspended = true;
            button.Loaded -= OnLoaded;
            button.Unloaded -= OnUnloaded;
            BindingOperations.ClearBinding(button, ObservedPressedProperty);
            Reset();
        }
        private void ObservePressed() => BindingOperations.SetBinding(button, ObservedPressedProperty,
            new Binding(nameof(ButtonBase.IsPressed)) { Source = button, Mode = BindingMode.OneWay });
        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            suspended = false;
            ObservePressed();
            Update();
        }
        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            suspended = true;
            BindingOperations.ClearBinding(button, ObservedPressedProperty);
            Reset();
        }

        public void Reset()
        {
            animator?.CancelOwner(button, false);
            animator = null;
            initialized = false;
            if (scale is not null) scale.ScaleX = scale.ScaleY = 1;
            scale = null;
        }

        public void Update()
        {
            if (suspended) return;
            var next = GetAnimator(button);
            if (next is null || !next.MotionEnabled || !GetMotionEnabled(button) ||
                !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                Reset();
                return;
            }
            button.ApplyTemplate();
            var content = button.Template?.FindName("PressContent", button) as ContentPresenter;
            if (content?.RenderTransform is not ScaleTransform transform) return;
            if (transform.IsFrozen)
            {
                transform = transform.Clone();
                content.RenderTransform = transform;
            }
            if (!ReferenceEquals(next, animator) || !ReferenceEquals(transform, scale)) Reset();
            animator = next;
            scale = transform;
            if (!initialized || !animator.HasChannel(button, "press-content-scale"))
            {
                animator.SetImmediate(button, "press-content-scale", 1, ApplyScale);
                initialized = true;
            }
            animator.Retarget(button, "press-content-scale", button.IsPressed ? .985 : 1, SpringParameters.Press, ApplyScale);
        }
        private void ApplyScale(double value)
        {
            if (scale is not null)
                scale.ScaleX = scale.ScaleY = animator is { MotionEnabled: true } && GetMotionEnabled(button) &&
                    SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast ? value : 1;
        }
    }
}
