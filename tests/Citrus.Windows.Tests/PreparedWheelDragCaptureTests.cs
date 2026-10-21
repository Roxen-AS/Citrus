using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Forms = System.Windows.Forms;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class PreparedWheelDragCaptureTests(DesktopFixture desktop)
{
    [Fact]
    public Task Unbound_target_rejects_every_callback_without_reading_payload() => desktop.RunAsync(async () =>
    {
        var owner = new Window();
        WheelDragCapture? capture = null;
        try
        {
            capture = CreateUnbound(owner);
            var data = new Payload();
            Assert.False(capture.IsVisible);
            foreach (var kind in Enum.GetValues<WheelDragEventKind>())
            {
                var args = new Forms.DragEventArgs(data, 1, 0, 0, Forms.DragDropEffects.Copy, Forms.DragDropEffects.Copy);
                capture.Dispatch(kind, WheelDragInput.FromForms(args, Dispatcher.CurrentDispatcher));
                Assert.Equal(Forms.DragDropEffects.None, args.Effect);
            }
            Assert.Equal(0, data.Reads);
            Assert.Throws<InvalidOperationException>(capture.Start);
        }
        finally { capture?.Release(Task.CompletedTask); if (capture is not null) await capture.Completion; owner.Close(); }
    });

    [Fact]
    public Task Prepared_target_accepts_one_binding_and_cannot_bind_after_retirement() => desktop.RunAsync(async () =>
    {
        var owner = new Window();
        WheelDragCapture? capture = null;
        try
        {
            capture = CreateUnbound(owner);
            int first = 0, replacement = 0;
            Assert.True(Bind(capture, () => first++));
            Assert.False(Bind(capture, () => replacement++));
            capture.Dispatch(WheelDragEventKind.Over, WheelDragInput.Leave(Dispatcher.CurrentDispatcher));
            Assert.Equal(1, first);
            Assert.Equal(0, replacement);
            capture.Release(Task.CompletedTask);
            await capture.Completion;
            Assert.False(Bind(capture, () => replacement++));
        }
        finally { capture?.Release(Task.CompletedTask); if (capture is not null) await capture.Completion; owner.Close(); }
    });

    [Fact]
    public Task Prepared_target_rejects_a_recreated_form_handle_even_if_a_numeric_handle_is_reused() => desktop.RunAsync(async () =>
    {
        var owner = new Window();
        Forms.Form? form = null;
        WheelDragCapture? capture = null;
        try
        {
            capture = CreateUnbound(owner, new(value => { form = value; return value.Handle; }, _ => { }));
            Assert.NotNull(form);
            typeof(Forms.Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Assert.False(Bind(capture));
        }
        finally { capture?.Release(Task.CompletedTask); if (capture is not null) await capture.Completion; owner.Close(); }
    });

    [Fact]
    public Task Idle_preparation_keeps_one_hidden_unpublished_candidate_and_admission_consumes_it() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        QueuePreparation(fixture.Wheel);
        QueuePreparation(fixture.Wheel);
        await Idle();
        var candidate = Prepared(fixture.Wheel);
        Assert.NotNull(candidate);
        Assert.Equal(1, fixture.Created);
        Assert.False(candidate.IsVisible);
        Assert.Equal(0, fixture.Wheel.DragCaptureHandle);
        Assert.True(fixture.Wheel.GlassCompletion.IsCompleted);
        QueuePreparation(fixture.Wheel);
        await Idle();
        Assert.Same(candidate, Prepared(fixture.Wheel));
        Assert.Equal(1, fixture.Created);
        fixture.Enter();
        Assert.Same(candidate, fixture.Capture);
        Assert.Null(Prepared(fixture.Wheel));
        Assert.Equal(1, fixture.Created);
        Assert.True(candidate.IsVisible);
    });

    [Fact]
    public Task Idle_preparation_places_full_physical_desktop_bounds_while_hidden_and_unadmitted() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        QueuePreparation(fixture.Wheel);
        await Idle();
        var candidate = Prepared(fixture.Wheel);
        Assert.NotNull(candidate);
        Assert.Equal(new System.Drawing.Rectangle(NativeMethods.GetSystemMetrics(76), NativeMethods.GetSystemMetrics(77),
            Math.Max(1, NativeMethods.GetSystemMetrics(78)), Math.Max(1, NativeMethods.GetSystemMetrics(79))),
            Assert.Single(fixture.Placements));
        Assert.False(candidate.IsVisible);
        Assert.False(fixture.Forms[0].Visible);
        Assert.Equal(0, fixture.Wheel.DragCaptureHandle);
        Assert.True(fixture.Wheel.GlassCompletion.IsCompleted);
        Assert.Throws<InvalidOperationException>(candidate.Start);
    });

    [Fact]
    public Task Admission_refreshes_current_bounds_twice_after_hidden_placement() => desktop.RunAsync(async () =>
    {
        var bounds = new System.Drawing.Rectangle(40, 60, 320, 240);
        using var fixture = new Fixture(handle =>
        {
            Assert.True(NativeMethods.SetWindowPos(handle, new nint(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10));
        });
        QueuePreparation(fixture.Wheel);
        await Idle();
        Assert.Equal(bounds, Assert.Single(fixture.Placements));
        Assert.False(fixture.Forms[0].Visible);
        // Simulate changed current physical desktop coordinates between idle and admission.
        bounds = new(-120, 80, 640, 480);
        fixture.Enter();
        Assert.Equal(3, fixture.Placements.Count);
        Assert.Equal(bounds, fixture.Placements[1]);
        Assert.Equal(bounds, fixture.Placements[2]);
        Assert.True(fixture.Capture.IsVisible);
    });

    [Fact]
    public Task Idle_placement_failure_retires_unpublished_target_and_admission_creates_a_fresh_one() => desktop.RunAsync(async () =>
    {
        int placements = 0;
        using var fixture = new Fixture(handle =>
        {
            if (++placements == 1) throw new System.ComponentModel.Win32Exception(5, "Injected idle placement failure.");
            WheelDragCapturePlatform.Default.Place(handle);
        });
        QueuePreparation(fixture.Wheel);
        await Idle();
        Assert.Null(Prepared(fixture.Wheel));
        Assert.True(fixture.Forms[0].IsDisposed);
        Assert.Equal(0, fixture.Wheel.DragCaptureHandle);
        Assert.True(fixture.Wheel.DragRetirementCompletion.IsCompletedSuccessfully);
        fixture.Enter();
        Assert.Equal(2, fixture.Created);
        Assert.True(fixture.Capture.IsVisible);
    });

    [Fact]
    public Task Invalid_prepared_handle_is_retired_before_a_fresh_target_is_admitted() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        QueuePreparation(fixture.Wheel);
        await Idle();
        var candidate = Prepared(fixture.Wheel);
        Assert.NotNull(candidate);
        typeof(Forms.Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Forms[0], null);
        fixture.Enter();
        Assert.NotSame(candidate, fixture.Capture);
        Assert.Equal(2, fixture.Created);
        await candidate.Completion;
        Assert.True(fixture.Forms[0].IsDisposed);
        Assert.True(fixture.Capture.IsVisible);
    });

    [Fact]
    public Task Cancel_drain_does_not_prepare_and_stale_callbacks_cannot_read_a_reopened_generation() => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        QueuePreparation(fixture.Wheel);
        await Idle();
        fixture.Enter();
        var old = fixture.Capture;
        Assert.True(fixture.Wheel.TryObserveCancellation());
        QueuePreparation(fixture.Wheel);
        await Idle();
        Assert.Null(Prepared(fixture.Wheel));
        Assert.Equal(1, fixture.Created);
        fixture.Wheel.ObserveGlobalRelease();
        old.Dispatch(WheelDragEventKind.Drop, WheelDragInput.Leave(Dispatcher.CurrentDispatcher));
        await Idle();
        Assert.False(fixture.Wheel.IsVisible);
        var replacement = Prepared(fixture.Wheel);
        Assert.NotNull(replacement);
        Assert.NotSame(old, replacement);
        fixture.Enter();
        Assert.Same(replacement, fixture.Capture);
        var data = new Payload();
        var args = new Forms.DragEventArgs(data, 1, 0, 0, Forms.DragDropEffects.Copy, Forms.DragDropEffects.Copy);
        old.Dispatch(WheelDragEventKind.Enter, WheelDragInput.FromForms(args, Dispatcher.CurrentDispatcher));
        Assert.Equal(0, data.Reads);
        Assert.Equal(Forms.DragDropEffects.None, args.Effect);
        Assert.True(fixture.Wheel.IsVisible);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Shutdown_aborts_queued_preparation_and_joins_an_unconsumed_candidate(bool runIdle) => desktop.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        QueuePreparation(fixture.Wheel);
        if (runIdle) await Idle();
        var candidate = Prepared(fixture.Wheel);
        fixture.Wheel.ReleasePresentation();
        await fixture.Wheel.DragRetirementCompletion;
        await Idle();
        Assert.Null(Prepared(fixture.Wheel));
        Assert.Equal(runIdle ? 1 : 0, fixture.Created);
        if (candidate is not null) Assert.True(candidate.Completion.IsCompletedSuccessfully);
        Assert.All(fixture.Forms, form => Assert.True(form.IsDisposed));
    });

    private static WheelDragCapture CreateUnbound(Window owner, WheelDragCapturePlatform? platform = null)
    {
        var constructor = typeof(WheelDragCapture).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(Window), typeof(WheelDragCapturePlatform)], null);
        Assert.NotNull(constructor);
        return (WheelDragCapture)constructor.Invoke([owner, platform ?? WheelDragCapturePlatform.Default]);
    }

    private static bool Bind(WheelDragCapture capture, Action? callback = null)
    {
        var method = typeof(WheelDragCapture).GetMethod("TryBind", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method.Invoke(capture, [(Action<WheelDragInput, bool>)((_, _) => callback?.Invoke()),
            (Action<WheelDragInput>)(_ => callback?.Invoke()), (Action<WheelDragInput>)(_ => callback?.Invoke())])!;
    }

    private static void QueuePreparation(WheelWindow wheel)
    {
        var method = typeof(WheelWindow).GetMethod("QueueDragCapturePreparation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(wheel, null);
    }

    private static WheelDragCapture? Prepared(WheelWindow wheel)
    {
        var field = typeof(WheelWindow).GetField("preparedCapture", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (WheelDragCapture?)field.GetValue(wheel);
    }

    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.SystemIdle);

    private sealed class Fixture : IDisposable
    {
        private readonly string file = Path.Combine(Path.GetTempPath(), "citrus-prepared-" + Guid.NewGuid().ToString("N") + ".txt");
        public WheelWindow Wheel { get; }
        public List<Forms.Form> Forms { get; } = [];
        public List<System.Drawing.Rectangle> Placements { get; } = [];
        public int Created => Forms.Count;
        public WheelDragCapture Capture => (WheelDragCapture)typeof(WheelWindow).GetField("outsideCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Wheel)!;
        public Fixture(Action<nint>? place = null)
        {
            File.WriteAllText(file, "physical payload");
            var platform = new WheelDragCapturePlatform(form => { Forms.Add(form); return form.Handle; }, handle =>
            {
                (place ?? WheelDragCapturePlatform.Default.Place)(handle);
                Placements.Add(Forms.Single(form => form.IsHandleCreated && form.Handle == handle).Bounds);
            });
            Wheel = new(new OperationCatalog([]), null, null, null, dragSource: () => new Source(),
                physicalLeftButtonDown: () => true, dragCapturePlatform: platform);
            Wheel.ApplySettings(new() { ReduceMotion = true, SoundEnabled = false });
            Wheel.PrepareHiddenPresentation();
        }
        public void Enter()
        {
            Wheel.ShowAtPointer(OperationMode.Convert);
            var surface = (Canvas)Wheel.FindName("Surface");
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { file }),
                    DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, surface, new System.Windows.Point(184, 184)], null)!;
            args.RoutedEvent = System.Windows.DragDrop.PreviewDragEnterEvent;
            surface.RaiseEvent(args);
            Assert.True(args.Handled);
            Assert.True(Wheel.IsVisible);
            Assert.NotEqual(0, Wheel.DragCaptureHandle);
        }
        public void Dispose() { Wheel.ReleasePresentation(); Wheel.Close(); File.Delete(file); }
    }

    private sealed class Source : IDragSourceLifetime
    {
        public bool HasCompleted(bool leftReleased) => false;
        public void Dispose() { }
    }

#pragma warning disable WFO1001
    private sealed class Payload : Forms.IDataObject
    {
        public int Reads { get; private set; }
        public object GetData(string format, bool autoConvert) { Reads++; throw new InvalidOperationException("Unadmitted payload was read."); }
        public object GetData(string format) => GetData(format, false);
        public object GetData(Type format) => GetData(Forms.DataFormats.FileDrop, false);
        public bool GetDataPresent(string format, bool autoConvert) { Reads++; throw new InvalidOperationException("Unadmitted payload was queried."); }
        public bool GetDataPresent(string format) => GetDataPresent(format, false);
        public bool GetDataPresent(Type format) => GetDataPresent(Forms.DataFormats.FileDrop, false);
        public string[] GetFormats(bool autoConvert) => [Forms.DataFormats.FileDrop];
        public string[] GetFormats() => GetFormats(false);
        public void SetData(string format, bool autoConvert, object? data) => throw new NotSupportedException();
        public void SetData(string format, object? data) => throw new NotSupportedException();
        public void SetData(Type format, object? data) => throw new NotSupportedException();
        public void SetData(object? data) => throw new NotSupportedException();
    }
#pragma warning restore WFO1001
}
