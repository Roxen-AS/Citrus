using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using Forms = System.Windows.Forms;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelDragCaptureFailureTests(DesktopFixture desktop)
{
    [Fact]
    public Task Handle_creation_failure_disposes_the_partially_created_native_target() => desktop.RunAsync(() =>
    {
        var owner = new Window();
        Forms.Form? form = null;
        nint handle = 0;
        try
        {
            var platform = Platform(value =>
            {
                form = value; handle = value.Handle;
                throw new Win32Exception(8, "Injected native handle failure");
            }, PlaceNative);
            var error = Assert.Throws<TargetInvocationException>(() => CreateCapture(owner, platform));
            Assert.IsType<Win32Exception>(error.InnerException);
            Assert.NotNull(form);
            Assert.True(form.IsDisposed);
            Assert.False(BackdropNativeMethods.IsWindow(handle));
        }
        finally { form?.Dispose(); owner.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Placement_failure_after_show_rejects_input_until_exclusion_safe_disposal() => desktop.RunAsync(async () =>
    {
        var owner = new Window();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WheelDragCapture? capture = null;
        Forms.Form? form = null;
        int placements = 0, callbacks = 0;
        try
        {
            new WindowInteropHelper(owner).EnsureHandle();
            var platform = Platform(value => { form = value; return value.Handle; }, handle =>
            {
                if (++placements == 2)
                {
                    Assert.True(form!.Visible);
                    throw new Win32Exception(5, "Injected placement failure after Show");
                }
                PlaceNative(handle);
            });
            capture = CreateCapture(owner, platform, () => callbacks++);
            nint original = capture.Handle;
            Assert.Throws<Win32Exception>(capture.Start);
            Assert.False(capture.IsVisible);
            Assert.False(capture.AllowDrop);
            Assert.Equal(nint.Zero, BackdropNativeMethods.GetProp(original, "OleDropTargetInterface"));
            var args = new Forms.DragEventArgs(null, 1, 0, 0, Forms.DragDropEffects.Copy, Forms.DragDropEffects.Copy);
            capture.Dispatch(WheelDragEventKind.Enter, WheelDragInput.FromForms(args, Dispatcher.CurrentDispatcher));
            Assert.Equal(0, callbacks);
            Assert.Equal(Forms.DragDropEffects.None, args.Effect);
            capture.Release(gate.Task);
            owner.Close();
            Assert.True(BackdropNativeMethods.IsWindow(original));
            Assert.False(capture.Completion.IsCompleted);
            gate.SetResult();
            await capture.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(BackdropNativeMethods.IsWindow(original));
        }
        finally
        {
            gate.TrySetResult(); capture?.Release(Task.CompletedTask);
            owner.Close();
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Admission_failure_rejects_the_drag_and_allows_a_later_activation(bool failHandle) => desktop.RunAsync(async () =>
    {
        string file = Path.Combine(Path.GetTempPath(), "citrus-admission-" + Guid.NewGuid().ToString("N") + ".txt");
        WheelWindow? wheel = null;
        int handles = 0, placements = 0, drops = 0;
        try
        {
            File.WriteAllText(file, "physical payload");
            var platform = Platform(value =>
            {
                nint handle = value.Handle;
                if (failHandle && ++handles == 1) throw new Win32Exception(8, "Injected admission handle failure");
                return handle;
            }, handle =>
            {
                if (!failHandle && ++placements == 2) throw new Win32Exception(5, "Injected admission placement failure");
                PlaceNative(handle);
            });
            wheel = CreateWheel(platform);
            wheel.OperationDropped += _ => drops++;
            wheel.ApplySettings(new() { ReduceMotion = true, SoundEnabled = false });
            wheel.ShowAtPointer(OperationMode.Convert);
            var error = Record.Exception(() => Enter(wheel, file));
            Assert.Null(error);
            Assert.False(wheel.IsVisible);
            Assert.Equal(nint.Zero, wheel.DragCaptureHandle);
            Assert.False(((WheelSession)typeof(WheelWindow).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel)!).IsConfirmed);
            await Task.WhenAll(wheel.GlassRetirementCompletion, wheel.DragRetirementCompletion).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, drops);
            wheel.ShowAtPointer(OperationMode.Convert);
            Assert.Null(Record.Exception(() => Enter(wheel, file)));
            Assert.NotEqual(nint.Zero, wheel.DragCaptureHandle);
            Assert.True(wheel.IsVisible);
        }
        finally
        {
            if (wheel is not null)
            {
                wheel.ReleasePresentation(); wheel.Close();
                await Task.WhenAll(wheel.GlassRetirementCompletion, wheel.DragRetirementCompletion).WaitAsync(TimeSpan.FromSeconds(5));
            }
            File.Delete(file);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Queued_desktop_placement_failure_rejects_admission_and_releases_target(bool cancellationDraining) => desktop.RunAsync(async () =>
    {
        string file = Path.Combine(Path.GetTempPath(), "citrus-reposition-" + Guid.NewGuid().ToString("N") + ".txt");
        WheelWindow? wheel = null;
        int placements = 0;
        try
        {
            File.WriteAllText(file, "physical payload");
            var platform = Platform(value => value.Handle, handle =>
            {
                if (++placements == 3) throw new Win32Exception(5, "Injected placement failure after admission");
                PlaceNative(handle);
            });
            wheel = CreateWheel(platform, () => true);
            wheel.ShowAtPointer(OperationMode.Convert);
            Enter(wheel, file);
            var capture = (WheelDragCapture)typeof(WheelWindow).GetField("outsideCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel)!;
            if (cancellationDraining) Assert.True(wheel.TryObserveCancellation());
            // Establish the guarded queued path before sending a native message;
            // the old unguarded callback would terminate the shared dispatcher.
            Assert.NotNull(typeof(WheelDragCapture).GetMethod("RefreshDesktop", BindingFlags.Instance | BindingFlags.NonPublic));
            SendMessage(capture.Handle, 0x007E, 0, 0); // WM_DISPLAYCHANGE.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(wheel.IsVisible);
            Assert.Equal(nint.Zero, wheel.DragCaptureHandle);
            Assert.False(capture.IsVisible);
            Assert.False(capture.AllowDrop);
            await Task.WhenAll(wheel.GlassRetirementCompletion, wheel.DragRetirementCompletion).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(nint.Zero, capture.Handle);
        }
        finally
        {
            if (wheel is not null)
            {
                wheel.ReleasePresentation(); wheel.Close();
                await Task.WhenAll(wheel.GlassRetirementCompletion, wheel.DragRetirementCompletion).WaitAsync(TimeSpan.FromSeconds(5));
            }
            File.Delete(file);
        }
    });

    private static object Platform(Func<Forms.Form, nint> createHandle, Action<nint> place)
    {
        var type = typeof(WheelDragCapture).Assembly.GetType("Citrus.Windows.Presentation.WheelDragCapturePlatform");
        Assert.NotNull(type);
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [createHandle, place], null)!;
    }

    private static WheelDragCapture CreateCapture(Window owner, object platform, Action? callback = null)
    {
        var ctor = typeof(WheelDragCapture).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(value => value.GetParameters().Length == 5);
        Assert.NotNull(ctor);
        return (WheelDragCapture)ctor.Invoke([owner, (Action<WheelDragInput, bool>)((_, _) => callback?.Invoke()),
            (Action<WheelDragInput>)(_ => callback?.Invoke()), (Action<WheelDragInput>)(_ => callback?.Invoke()), platform]);
    }

    private static WheelWindow CreateWheel(object platform, Func<bool>? leftButtonDown = null)
    {
        var ctor = typeof(WheelWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(value => value.GetParameters().Any(parameter => parameter.Name == "dragCapturePlatform"));
        Assert.NotNull(ctor);
        return (WheelWindow)ctor.Invoke(ctor.GetParameters().Select(parameter => parameter.Name switch
        {
            "catalog" => (object)new OperationCatalog([]),
            "dragCapturePlatform" => platform,
            "physicalLeftButtonDown" => leftButtonDown,
            _ => parameter.HasDefaultValue ? parameter.DefaultValue : null
        }).ToArray());
    }

    private static void Enter(WheelWindow wheel, string file)
    {
        var surface = (Canvas)wheel.FindName("Surface");
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { file }), DragDropKeyStates.LeftMouseButton,
                DragDropEffects.Copy, surface, new Point(184, 184)], null)!;
        args.RoutedEvent = DragDrop.PreviewDragEnterEvent;
        surface.RaiseEvent(args);
        Assert.True(args.Handled);
        Assert.Equal(DragDropEffects.None, args.Effects);
    }

    private static void PlaceNative(nint handle)
    {
        if (!NativeMethods.SetWindowPos(handle, new nint(-1), NativeMethods.GetSystemMetrics(76), NativeMethods.GetSystemMetrics(77),
            Math.Max(1, NativeMethods.GetSystemMetrics(78)), Math.Max(1, NativeMethods.GetSystemMetrics(79)), 0x10))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nuint wparam, nint lparam);
}
