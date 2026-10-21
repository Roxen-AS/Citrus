using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Window = System.Windows.Window;
using Forms = System.Windows.Forms;
using Expression = System.Linq.Expressions.Expression;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelDragTargetPerformanceTests(DesktopFixture desktop)
{
    [Fact]
    public void Desktop_drag_proxy_does_not_use_a_Wpf_per_pixel_window()
        => Assert.False(typeof(Window).IsAssignableFrom(typeof(WheelDragCapture)));

    [Fact]
    public Task Hidden_preparation_creates_the_wheel_handle_without_showing_or_starting_capture() => desktop.RunAsync(() =>
    {
        var prepare = typeof(WheelWindow).GetMethod("PrepareHiddenPresentation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(prepare);
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            prepare.Invoke(wheel, null);
            Assert.NotEqual(nint.Zero, new WindowInteropHelper(wheel).Handle);
            Assert.False(wheel.IsVisible);
            Assert.Equal(nint.Zero, wheel.DragCaptureHandle);
            Assert.True(wheel.GlassCompletion.IsCompleted);
        }
        finally { wheel.ReleasePresentation(); wheel.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Uniform_alpha_proxy_is_hidden_until_start_and_keeps_its_handle_until_exclusion_releases() => desktop.RunAsync(async () =>
    {
        var release = typeof(WheelDragCapture).GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(Task)], null);
        var handleProperty = typeof(WheelDragCapture).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
        var visible = typeof(WheelDragCapture).GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.NonPublic);
        var completion = typeof(WheelDragCapture).GetProperty("Completion", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(release); Assert.NotNull(handleProperty); Assert.NotNull(visible); Assert.NotNull(completion);
        var owner = new Window();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        object? capture = null;
        try
        {
            new WindowInteropHelper(owner).EnsureHandle();
            var ctor = typeof(WheelDragCapture).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(value => value.GetParameters().Length == 4);
            var arguments = new object?[] { owner }.Concat(ctor.GetParameters().Skip(1).Select(parameter =>
            {
                var signature = parameter.ParameterType.GetMethod("Invoke")!;
                var parameters = signature.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
                return (object?)Expression.Lambda(parameter.ParameterType, Expression.Empty(), parameters).Compile();
            })).ToArray();
            capture = ctor.Invoke(arguments);
            nint handle = (nint)handleProperty.GetValue(capture)!;
            Assert.NotEqual(nint.Zero, handle);
            Assert.False((bool)visible.GetValue(capture)!);
            Assert.True(GetLayeredWindowAttributes(handle, out _, out byte alpha, out uint flags));
            Assert.Equal((byte)1, alpha); Assert.Equal(2u, flags);
            long styles = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
            Assert.NotEqual(0, styles & 0x00080000); // WS_EX_LAYERED.
            Assert.NotEqual(0, styles & 0x08000000); // WS_EX_NOACTIVATE.
            Assert.NotEqual(0, styles & 0x00000080); // WS_EX_TOOLWINDOW.
            Assert.Equal(0, styles & 0x00040000); // No WS_EX_APPWINDOW taskbar entry.
            Assert.Equal(0, styles & 0x00000020); // No WS_EX_TRANSPARENT input passthrough.
            Assert.Equal(nint.Zero, NativeMethods.GetWindowLongPtr(handle, -8)); // No per-form hidden taskbar owner.
            Assert.NotEqual(nint.Zero, BackdropNativeMethods.GetProp(handle, "OleDropTargetInterface"));
            NativeMethods.SetWindowLongPtr(handle, -8, new WindowInteropHelper(owner).Handle);
            release.Invoke(capture, [gate.Task]);
            Assert.False((bool)visible.GetValue(capture)!);
            owner.Close(); // Native ownership must detach before this destroys its HWND.
            Assert.True(BackdropNativeMethods.IsWindow(handle));
            Assert.Equal(nint.Zero, BackdropNativeMethods.GetProp(handle, "OleDropTargetInterface"));
            gate.SetResult();
            await ((Task)completion.GetValue(capture)!).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(BackdropNativeMethods.IsWindow(handle));
        }
        finally
        {
            gate.TrySetResult();
            if (capture is not null) release.Invoke(capture, [Task.CompletedTask]);
            owner.Close();
        }
    });

    [Fact]
    public Task Forms_adapter_keeps_payload_extraction_lazy_and_preserves_screen_point_and_effects() => desktop.RunAsync(() =>
    {
        var adapter = typeof(WheelWindow).Assembly.GetType("Citrus.Windows.Presentation.WheelDragInput");
        Assert.NotNull(adapter);
        var fromForms = adapter.GetMethod("FromForms", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(fromForms);
        var data = new FormsPayload();
        var args = new Forms.DragEventArgs(data, 1 | 4 | 8, -142, 287, Forms.DragDropEffects.Copy | Forms.DragDropEffects.Move, Forms.DragDropEffects.None);
        object input = fromForms.Invoke(null, [args, Dispatcher.CurrentDispatcher])!;
        Assert.Equal(0, data.Reads);
        Assert.Equal(new System.Windows.Point(-142, 287), adapter.GetProperty("ScreenPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input));
        Assert.Equal(3, Convert.ToInt32(adapter.GetProperty("AllowedEffects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)));
        Assert.Equal(13, Convert.ToInt32(adapter.GetProperty("KeyStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)));
        Assert.Equal(new[] { @"C:\owned\first.txt", @"C:\owned\second.txt" },
            Assert.IsType<string[]>(adapter.GetMethod("ReadFiles", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, null)));
        Assert.Equal(2, data.Reads);
        adapter.GetProperty("Effects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(input, DragDropEffects.Copy);
        adapter.GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, null);
        Assert.Equal(Forms.DragDropEffects.Copy, args.Effect);
        return Task.CompletedTask;
    });

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint colorKey, out byte alpha, out uint flags);

    // This file-drop-only counter is deliberately a minimal legacy OLE test double;
    // it returns owned string arrays and never deserializes arbitrary data.
#pragma warning disable WFO1001
    private sealed class FormsPayload : Forms.IDataObject
    {
        public int Reads { get; private set; }
        public object GetData(string format, bool autoConvert) { Reads++; return new[] { @"C:\owned\first.txt", @"C:\owned\second.txt" }; }
        public object GetData(string format) => GetData(format, false);
        public object GetData(Type format) => GetData(Forms.DataFormats.FileDrop, false);
        public bool GetDataPresent(string format, bool autoConvert) { Reads++; return format == Forms.DataFormats.FileDrop; }
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
