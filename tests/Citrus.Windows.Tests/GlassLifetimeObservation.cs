using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Citrus.Core;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

/// <summary>Opt-in LifecycleHost diagnostics on the actual resident; no product test flags.</summary>
internal sealed class GlassLifetimeObservation
{
    private readonly WheelWindow wheel;
    private readonly SpringAnimator animator;
    private nint handle;
    public GlassLifetimeObservation(App app)
    {
        wheel = app.Windows.OfType<WheelWindow>().Single();
        animator = PressFeedback.GetAnimator(wheel) ?? throw new InvalidOperationException("Resident animator missing.");
    }
    public string Prepare(string sample)
    {
        wheel.ShowAtPointer(OperationMode.Actions);
        var surface = (Canvas)wheel.FindName("Surface");
        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { sample });
        var args = (System.Windows.DragEventArgs)Activator.CreateInstance(typeof(System.Windows.DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [data, System.Windows.DragDropKeyStates.LeftMouseButton, System.Windows.DragDropEffects.Copy, surface, new Point(184, 184)], null)!;
        args.RoutedEvent = System.Windows.DragDrop.PreviewDragEnterEvent;
        surface.RaiseEvent(args);
        animator.SetImmediate(surface, "quit-lifetime", 0, _ => { });
        animator.Retarget(surface, "quit-lifetime", 1, SpringParameters.Default, _ => { });
        handle = new WindowInteropHelper(wheel).Handle;
        return JsonSerializer.Serialize(new
        {
            Wheel = handle.ToInt64(), wheel.IsVisible, wheel.MaterialPresentationPending,
            MaterialState = wheel.MaterialState.ToString(),
            animator.ActiveChannelCount, animator.RetainedChannelCount, animator.IsRenderingSubscribed,
            Companion = wheel.CompanionHandle.ToInt64(), Admission = "Synthetic FileDrop on actual resident wheel; no external drag or optical proof"
        });
    }
    public string AfterQuit() => JsonSerializer.Serialize(new
    {
        WheelHwndAlive = BackdropNativeMethods.IsWindow(handle),
        wheel.MaterialPresentationPending, Companion = wheel.CompanionHandle.ToInt64(),
        AnimatorAttached = PressFeedback.GetAnimator(wheel) is not null,
        animator.ActiveChannelCount, animator.RetainedChannelCount, animator.IsRenderingSubscribed,
        Scope = "Actual App.QuitAsync and App.OnExit returned from app.Run; sampled before process exit"
    });
}
