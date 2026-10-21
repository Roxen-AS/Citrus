using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using IDataObject = System.Windows.IDataObject;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelCancellationTests(DesktopFixture desktop)
{
    [Fact]
    public Task Claimed_right_pair_survives_pause_and_repeated_down_cannot_cancel_a_replacement() => desktop.RunAsync(() =>
    {
        using var monitor = new GestureMonitor(Dispatcher.CurrentDispatcher);
        int claims = 0;
        monitor.RightButtonCancellationRequested += () => { claims++; return true; };
        Assert.Equal((nint)1, Mouse(monitor, 0x204));
        Assert.Equal((nint)1, Mouse(monitor, 0x204));
        Assert.Equal(1, claims);
        monitor.SetEnabled(false);
        Assert.Equal((nint)1, Mouse(monitor, 0x205));
        monitor.SetEnabled(true);
        Assert.Equal((nint)1, Mouse(monitor, 0x204));
        Assert.Equal(2, claims);
        Assert.Equal((nint)1, Mouse(monitor, 0x205));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Ordinary_pairs_and_negative_hook_codes_do_not_claim_cancellation() => desktop.RunAsync(() =>
    {
        using var monitor = new GestureMonitor(Dispatcher.CurrentDispatcher);
        int claims = 0;
        monitor.RightButtonCancellationRequested += () => { claims++; return false; };
        Assert.Equal((nint)0, Mouse(monitor, 0x204));
        Assert.Equal((nint)0, Mouse(monitor, 0x205));
        Assert.Equal(1, claims);
        Assert.Equal((nint)0, Mouse(monitor, 0x204, code: -1));
        Assert.Equal(1, claims);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Invisible_threshold_candidate_cannot_claim_a_right_pair() => desktop.RunAsync(() =>
    {
        using var fixture = new Fixture();
        fixture.Wheel.ShowAtPointer(OperationMode.Convert);
        Assert.Equal(0, fixture.Presentation.Opacity);
        Assert.False(fixture.Wheel.TryObserveCancellation());
        Assert.True(fixture.Wheel.IsVisible);
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        return Task.CompletedTask;
    });

    [Fact]
    public Task Cancellation_hides_presentation_synchronously_and_retains_the_capture_through_timer_and_pause() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        nint handle = fixture.Wheel.DragCaptureHandle;
        Assert.NotEqual(0, handle);
        var events = new List<OperationDrop>(); fixture.Wheel.OperationDropped += events.Add;
        Assert.True(fixture.Wheel.TryObserveCancellation());
        Assert.Equal(0, fixture.Presentation.Opacity);
        Assert.True(fixture.Wheel.IsVisible);
        Assert.Equal(handle, fixture.Wheel.DragCaptureHandle);
        fixture.Wheel.ShowAtPointer(OperationMode.Actions);
        fixture.Wheel.Dismiss(); // Pausing must not release a draining external drag.
        await Task.Delay(350);
        Assert.True(fixture.Wheel.IsVisible);
        Assert.Equal(handle, fixture.Wheel.DragCaptureHandle);
        Assert.Empty(events);
        Assert.False(fixture.Source.Disposed);
    });

    [Fact]
    public Task Cancel_release_recovery_does_not_require_source_identity_or_an_OLE_leave() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        nint capture = fixture.Wheel.DragCaptureHandle;
        var events = new List<OperationDrop>(); fixture.Wheel.OperationDropped += events.Add;
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Wheel.ObserveGlobalRelease();
        await Task.Delay(400);
        Assert.False(fixture.Wheel.IsVisible);
        Assert.Equal(0, fixture.Wheel.DragCaptureHandle);
        Assert.False(BackdropNativeMethods.IsWindow(capture));
        Assert.True(fixture.Source.Disposed);
        Assert.Empty(events);
        fixture.Data.ThrowOnRead = true;
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDropEvent).Effects);
    });

    [Fact]
    public Task Cancel_release_recovery_after_pause_rejects_queued_callbacks_without_reading_payload() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        var capture = fixture.Capture;
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Wheel.Dismiss(); // A pause does not revoke the held drag target prematurely.
        fixture.Wheel.ObserveGlobalRelease();
        fixture.Data.ThrowOnRead = true;
        int reads = fixture.Data.Reads;
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDragOverEvent, capture).Effects);
        Assert.Equal(reads, fixture.Data.Reads);
        Assert.True(capture.IsVisible); // Give OLE the terminal rejection interval first.
        await Task.Delay(400);
        Assert.False(capture.IsVisible);
        Assert.False(fixture.Wheel.IsVisible);
    });

    [Fact]
    public Task Cancel_release_recovery_handles_left_up_before_cancellation() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        fixture.Source.LeftButtonDown = false;
        Assert.True(fixture.Wheel.TryObserveCancellation());
        await Task.Delay(400);
        Assert.False(fixture.Wheel.IsVisible);
        Assert.Equal(0, fixture.Wheel.DragCaptureHandle);
    });

    [Fact]
    public Task Cancel_release_recovery_probes_a_missed_hook_release() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        await Task.Delay(300);
        Assert.True(fixture.Wheel.IsVisible);
        fixture.Source.LeftButtonDown = false; // No ObserveGlobalRelease: a missed or removed hook.
        await Task.Delay(650);
        Assert.False(fixture.Wheel.IsVisible);
        Assert.Equal(0, fixture.Wheel.DragCaptureHandle);
    });

    [Fact]
    public Task Cancel_release_recovery_timer_cannot_hide_a_replacement_generation() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Wheel.ObserveGlobalRelease();
        fixture.Raise(DragDrop.PreviewDropEvent, fixture.Capture);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(fixture.Wheel.IsVisible);
        fixture.Enter();
        await Task.Delay(400);
        Assert.True(fixture.Wheel.IsVisible);
        Assert.Equal(1, fixture.Presentation.Opacity);
    });

    [Fact]
    public Task Draining_hover_enter_and_drop_reject_without_reading_payload_then_teardown_after_callback() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Data.ThrowOnRead = true;
        int reads = fixture.Data.Reads;
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDragEnterEvent).Effects);
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDragOverEvent).Effects);
        var capture = fixture.Capture;
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDragEnterEvent, capture).Effects);
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDragOverEvent, capture).Effects);
        Assert.Equal(DragDropEffects.None, fixture.Raise(DragDrop.PreviewDropEvent, capture).Effects);
        Assert.Equal(reads, fixture.Data.Reads);
        Assert.True(capture.IsVisible); // Target ownership survives the active OLE callback.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(capture.IsVisible);
        Assert.False(fixture.Wheel.IsVisible);
        Assert.True(fixture.Source.Disposed);
    });

    [Fact]
    public Task Leave_before_release_retains_a_held_drag_and_replacement_is_not_hidden_by_old_probe() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        fixture.Source.RequireReleaseEvidence = true;
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Raise(DragDrop.PreviewDragLeaveEvent, fixture.Capture);
        await Task.Delay(300);
        Assert.NotEqual(0, fixture.Wheel.DragCaptureHandle);
        fixture.Source.Completed = true;
        fixture.Wheel.ObserveGlobalRelease();
        await Task.Delay(350);
        Assert.False(fixture.Wheel.IsVisible);
        Assert.True(fixture.Source.Disposed);
        fixture.Enter();
        await Task.Delay(350);
        Assert.True(fixture.Wheel.IsVisible);
        Assert.Equal(1, fixture.Presentation.Opacity);
    });

    [Fact]
    public Task Corroborated_source_end_on_leave_waits_until_callback_returns() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Source.RequireReleaseEvidence = true;
        fixture.Source.Completed = true;
        fixture.Wheel.ObserveGlobalRelease();
        var capture = fixture.Capture;
        fixture.Raise(DragDrop.PreviewDragLeaveEvent, capture, DragDropKeyStates.None);
        Assert.True(capture.IsVisible);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(fixture.Wheel.IsVisible);
    });

    [Fact]
    public Task Wpf_native_leave_without_button_state_cannot_release_a_still_held_drag() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Source.RequireReleaseEvidence = true;
        fixture.Source.Completed = true; // Capture disappearance alone is insufficient while still held.
        var capture = fixture.Capture;
        // WPF's native IOleDropTarget.OleDragLeave always supplies None, even during HWND handoff.
        fixture.Raise(DragDrop.PreviewDragLeaveEvent, capture, DragDropKeyStates.None);
        await Task.Delay(350);
        Assert.True(capture.IsVisible);
        fixture.Wheel.ObserveGlobalRelease();
        await Task.Delay(350);
        Assert.False(capture.IsVisible);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Release_recovery_does_not_wait_for_a_new_leave_after_reentry_or_hover(bool enter) => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        fixture.Source.RequireReleaseEvidence = true;
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Raise(DragDrop.PreviewDragLeaveEvent, fixture.Capture);
        fixture.Raise(enter ? DragDrop.PreviewDragEnterEvent : DragDrop.PreviewDragOverEvent, fixture.Capture);
        await Task.Delay(300);
        Assert.True(fixture.Wheel.IsVisible);
        fixture.Wheel.ObserveGlobalRelease();
        fixture.Source.Completed = true;
        await Task.Delay(350);
        Assert.False(fixture.Wheel.IsVisible);
        Assert.Equal(0, fixture.Wheel.DragCaptureHandle);
    });

    [Fact]
    public Task Reentry_invalidates_queued_left_up_leave_cleanup_before_it_can_release_the_target() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        fixture.Source.RequireReleaseEvidence = true;
        fixture.Source.Completed = true;
        fixture.Wheel.ObserveGlobalRelease();
        var capture = fixture.Capture;
        fixture.Raise(DragDrop.PreviewDragLeaveEvent, capture, DragDropKeyStates.None);
        fixture.Raise(DragDrop.PreviewDragEnterEvent, capture);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.True(capture.IsVisible);
        fixture.Raise(DragDrop.PreviewDropEvent, capture);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(capture.IsVisible);
    });

    [Fact]
    public Task Source_exit_corroboration_releases_capture_without_a_left_up_or_blind_timeout() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        await Task.Delay(300);
        Assert.True(fixture.Wheel.IsVisible);
        fixture.Source.Completed = true;
        await Task.Delay(350);
        Assert.False(fixture.Wheel.IsVisible);
    });

    [Fact]
    public Task Shutdown_releases_draining_capture_and_source_identity() => desktop.RunAsync(() =>
    {
        using var fixture = new Fixture();
        fixture.Enter();
        Assert.True(fixture.Wheel.TryObserveCancellation());
        var capture = fixture.Capture;
        fixture.Wheel.ReleasePresentation();
        Assert.False(capture.IsVisible);
        Assert.Equal(0, fixture.Wheel.DragCaptureHandle);
        Assert.True(fixture.Source.Disposed);
        return Task.CompletedTask;
    });

    private static nint Mouse(GestureMonitor monitor, int message, int code = 0)
    {
        nint data = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.MouseData>());
        try
        {
            Marshal.StructureToPtr(new NativeMethods.MouseData(), data, false);
            return (nint)typeof(GestureMonitor).GetMethod("Mouse", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monitor, [code, (nint)message, data])!;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    private sealed class Source : IDragSourceLifetime
    {
        public bool Completed { get; set; }
        public bool LeftButtonDown { get; set; } = true;
        public bool RequireReleaseEvidence { get; set; }
        public bool Disposed { get; private set; }
        public bool HasCompleted(bool leftReleased) => Completed && (!RequireReleaseEvidence || leftReleased);
        public void Dispose() => Disposed = true;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string file = Path.Combine(Path.GetTempPath(), "citrus-cancel-" + Guid.NewGuid().ToString("N") + ".txt");
        public WheelWindow Wheel { get; }
        public Source Source { get; } = new();
        public Payload Data { get; }
        public Canvas Presentation => (Canvas)Wheel.FindName("Presentation");
        public WheelDragCapture Capture => (WheelDragCapture)typeof(WheelWindow).GetField("outsideCapture", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Wheel)!;
        public Fixture()
        {
            File.WriteAllText(file, "physical payload"); Data = new(file);
            Wheel = new(new OperationCatalog([new Engine()]), null, null, null, dragSource: () => Source,
                physicalLeftButtonDown: () => Source.LeftButtonDown);
            Wheel.ApplySettings(new() { ReduceMotion = true, SoundEnabled = false });
        }
        public void Enter() { Wheel.ShowAtPointer(OperationMode.Convert); Raise(DragDrop.PreviewDragEnterEvent); }
        public DragEventArgs Raise(RoutedEvent routedEvent, object? target = null, DragDropKeyStates keys = DragDropKeyStates.LeftMouseButton)
        {
            var surface = (Canvas)Wheel.FindName("Surface");
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [Data, keys, DragDropEffects.Copy, surface, new Point(184, 184)], null)!;
            args.RoutedEvent = routedEvent;
            if (target is WheelDragCapture capture)
            {
                var kind = routedEvent == DragDrop.PreviewDragEnterEvent ? WheelDragEventKind.Enter :
                    routedEvent == DragDrop.PreviewDragOverEvent ? WheelDragEventKind.Over :
                    routedEvent == DragDrop.PreviewDropEvent ? WheelDragEventKind.Drop : WheelDragEventKind.Leave;
                capture.Dispatch(kind, WheelDragInput.FromWpf(args, surface, kind != WheelDragEventKind.Leave));
            }
            else surface.RaiseEvent(args);
            Assert.True(args.Handled); return args;
        }
        public void Dispose() { Wheel.ReleasePresentation(); Wheel.Close(); File.Delete(file); }
    }

    private sealed class Engine : IConversionEngine
    {
        public IReadOnlyList<OperationDescriptor> Operations { get; } = [new("convert", "Convert", OperationMode.Convert, null, false, false)];
        public bool CanExecute(OperationRequest request) => true;
        public Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token) => throw new InvalidOperationException();
    }

    private sealed class Payload(string file) : IDataObject
    {
        public int Reads { get; private set; }
        public bool ThrowOnRead { get; set; }
        public object GetData(string format, bool autoConvert)
        { Reads++; if (ThrowOnRead) throw new InvalidOperationException("Cancelled target read payload"); return new[] { file }; }
        public object GetData(string format) => GetData(format, false);
        public object GetData(Type format) => GetData(DataFormats.FileDrop, false);
        public bool GetDataPresent(string format, bool autoConvert)
        { if (ThrowOnRead) throw new InvalidOperationException("Cancelled target queried payload"); return format == DataFormats.FileDrop; }
        public bool GetDataPresent(string format) => GetDataPresent(format, false);
        public bool GetDataPresent(Type format) => GetDataPresent(DataFormats.FileDrop, false);
        public string[] GetFormats(bool autoConvert) => [DataFormats.FileDrop];
        public string[] GetFormats() => GetFormats(false);
        public void SetData(string format, object data, bool autoConvert) => throw new NotSupportedException();
        public void SetData(string format, object data) => throw new NotSupportedException();
        public void SetData(Type format, object data) => throw new NotSupportedException();
        public void SetData(object data) => throw new NotSupportedException();
    }
}
