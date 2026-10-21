using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;
using Forms = System.Windows.Forms;

namespace Citrus.Windows.Tests;

public sealed class NativeWheelCancellationFactAttribute : FactAttribute
{
    public NativeWheelCancellationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CITRUS_RUN_NATIVE_CANCEL_TESTS") != "1")
            Skip = "Opt-in owned native pointer proof; set CITRUS_RUN_NATIVE_CANCEL_TESTS=1. Routed callbacks do not prove WM_CONTEXTMENU suppression.";
    }
}

[Collection("Desktop")]
public sealed class NativeWheelCancellationTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [NativeWheelCancellationFact]
    public Task Cancelled_unknown_source_releases_the_desktop_target_and_next_native_left_click_reaches_owned_window() => desktop.RunAsync(async () =>
    {
        using var target = new ContextTarget();
        bool foregroundRequested = SetForegroundWindow(target.Handle);
        if (!foregroundRequested && Environment.GetEnvironmentVariable("CITRUS_NATIVE_FOREGROUND_HANDSHAKE") == "1")
        {
            // A normal activation may be refused by Windows foreground-lock policy.
            // An operator can activate this exact owned fixture through the supported
            // UI tools. Do not force foreground, retry activation, or emit input here.
            output.WriteLine($"NATIVE_FOREGROUND_WAIT hwnd={target.Handle.ToInt64()} pid={Environment.ProcessId} title=Citrus owned native cancellation proof");
            var waiting = System.Diagnostics.Stopwatch.StartNew();
            while (BackdropNativeMethods.GetForegroundWindow() != target.Handle && waiting.Elapsed < TimeSpan.FromSeconds(30))
                await Task.Delay(50);
            Assert.Equal(target.Handle, BackdropNativeMethods.GetForegroundWindow());
        }
        else Assert.True(foregroundRequested, "Owned native target could not become foreground; no input emitted.");
        await Task.Delay(100);
        Assert.True(GetClientRect(target.Handle, out var client));
        var point = new NativeMethods.Point { X = client.Right / 2, Y = client.Bottom / 2 };
        Assert.True(ClientToScreen(target.Handle, ref point));
        RequireOwned(target, point, requireCursor: false);
        Assert.True((NativeMethods.GetAsyncKeyState(1) & 0x8000) == 0 && (NativeMethods.GetAsyncKeyState(2) & 0x8000) == 0,
            "An existing pointer press prevents native recovery proof; no input emitted.");
        using var files = new NativeFiles();
        string path = files.File("cancel-recovery.txt");
        bool leftHeld = true, downSent = false;
        var wheel = new WheelWindow(new OperationCatalog([]), null, null, null,
            dragSource: () => new UnknownSource(), physicalLeftButtonDown: () => leftHeld);
        wheel.ApplySettings(new() { ReduceMotion = true, SoundEnabled = false });
        try
        {
            wheel.ShowAtPointer(OperationMode.Convert);
            var surface = (Canvas)wheel.FindName("Surface");
            var args = (System.Windows.DragEventArgs)Activator.CreateInstance(typeof(System.Windows.DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { path }),
                    System.Windows.DragDropKeyStates.LeftMouseButton, System.Windows.DragDropEffects.Copy, surface, new System.Windows.Point(184, 184)], null)!;
            args.RoutedEvent = System.Windows.DragDrop.PreviewDragEnterEvent;
            surface.RaiseEvent(args);
            Assert.True(args.Handled);
            nint capture = wheel.DragCaptureHandle;
            Assert.NotEqual(0, capture);
            Assert.True(wheel.TryObserveCancellation());
            leftHeld = false;
            wheel.ObserveGlobalRelease();
            await Task.Delay(400); // No terminal Drop, Leave or usable source identity.
            Assert.False(wheel.IsVisible);
            Assert.Equal(0, wheel.DragCaptureHandle);
            Assert.False(BackdropNativeMethods.IsWindow(capture));
            RequireOwned(target, point, requireCursor: false);
            Assert.True(SetCursorPos(point.X, point.Y));
            void Pointer(uint flag)
            {
                RequireOwned(target, point, requireCursor: true);
                MouseEvent(flag, 0, 0, 0, 0);
                downSent = flag == 0x0002;
            }
            try
            {
                Pointer(0x0002); await Task.Delay(100);
                Pointer(0x0004); await Task.Delay(100);
                Assert.Equal(1, target.LeftDowns);
                Assert.Equal(1, target.LeftUps);
            }
            finally { if (downSent) Pointer(0x0004); }
            output.WriteLine("NATIVE CLICK RECOVERY: routed admitted/cancelled wheel with unknown source and no Drop/Leave released its real full-desktop HWND; next guarded native left DOWN+UP reached the owned underlying window1each. Cancellation admission/release was controlled, not a physical Explorer drag.");
        }
        finally { wheel.ReleasePresentation(); wheel.Close(); }
    }, TimeSpan.FromSeconds(60));

    [NativeWheelCancellationFact]
    public Task Native_source_identity_keeps_capture_and_requires_independent_release_evidence() => desktop.RunAsync(async () =>
    {
        using var target = new ContextTarget();
        Assert.True(SetForegroundWindow(target.Handle), "Owned native target could not become foreground; no capture changed.");
        await Task.Delay(100);
        Assert.True(GetClientRect(target.Handle, out var client));
        var point = new NativeMethods.Point { X = client.Right / 2, Y = client.Bottom / 2 };
        Assert.True(ClientToScreen(target.Handle, ref point));
        RequireOwned(target, point, requireCursor: false);
        Assert.Equal(0, GetCapture());
        Assert.True((NativeMethods.GetAsyncKeyState(1) & 0x8000) == 0 && (NativeMethods.GetAsyncKeyState(2) & 0x8000) == 0,
            "An existing pointer press prevents native capture proof.");
        SetCapture(target.Handle);
        try
        {
            Assert.Equal(target.Handle, GetCapture());
            using var source = NativeDragSourceLifetime.Capture();
            Assert.False(source.HasCompleted(leftReleased: true)); // Retained original capture is still active.
            Assert.True(ReleaseCapture());
            Assert.False(source.HasCompleted(leftReleased: false)); // Capture disappearance alone is inconclusive.
            Assert.True(source.HasCompleted(leftReleased: true));
            source.Dispose();
            Assert.False(source.HasCompleted(leftReleased: true)); // Disposed identities cannot authorize a later generation.
            output.WriteLine("NATIVE SOURCE IDENTITY: real owned source HWND capture + retained process/thread handles; active capture blocks completion, released capture requires independent release evidence, disposed identity cannot complete. Controlled capture is not an Explorer/OLE completion proof.");
        }
        finally
        {
            if (GetCapture() == target.Handle) ReleaseCapture();
        }
    });

    [NativeWheelCancellationFact]
    public Task Claimed_native_pair_suppresses_contextmenu_and_next_ordinary_pair_recovers() => desktop.RunAsync(async () =>
    {
        using var target = new ContextTarget();
        Assert.True(SetForegroundWindow(target.Handle), "Owned native target could not become foreground; no input emitted.");
        await Task.Delay(100);
        Assert.True(GetClientRect(target.Handle, out var client));
        var point = new NativeMethods.Point { X = client.Right / 2, Y = client.Bottom / 2 };
        Assert.True(ClientToScreen(target.Handle, ref point));
        RequireOwned(target, point, requireCursor: false);
        Assert.True((NativeMethods.GetAsyncKeyState(1) & 0x8000) == 0 && (NativeMethods.GetAsyncKeyState(2) & 0x8000) == 0,
            "An existing pointer press prevents native proof; no input emitted.");
        Assert.True(SetCursorPos(point.X, point.Y));
        using var monitor = new GestureMonitor(Dispatcher.CurrentDispatcher);
        int claims = 0;
        monitor.RightButtonCancellationRequested += () => ++claims == 1;
        bool downSent = false;
        void Pointer(uint flag)
        {
            RequireOwned(target, point, requireCursor: true);
            MouseEvent(flag, 0, 0, 0, 0);
            downSent = flag == 0x0008;
        }
        try
        {
            Pointer(0x0008);
            await Task.Delay(100);
            Assert.Equal(1, claims);
            monitor.SetEnabled(false); // The owned UP must be consumed even after pause.
            Pointer(0x0010);
            await Task.Delay(100);
            Assert.Equal(0, target.Downs); Assert.Equal(0, target.Ups); Assert.Equal(0, target.ContextMenus);
            monitor.SetEnabled(true);
            Pointer(0x0008); await Task.Delay(100);
            Pointer(0x0010); await Task.Delay(100);
            Assert.Equal(2, claims);
            Assert.Equal(1, target.Downs); Assert.Equal(1, target.Ups); Assert.Equal(1, target.ContextMenus);
            output.WriteLine("NATIVE CANCEL PAIR: claimed DOWN+UP delivered 0 right messages and 0 WM_CONTEXTMENU; next unclaimed pair delivered 1 DOWN, 1 UP, 1 real WM_CONTEXTMENU. Native Explorer/OLE lifetime remains separate.");
        }
        finally
        {
            // Release only a press this test emitted while its actual native target is still owned.
            if (downSent) Pointer(0x0010);
        }
    });

    private static void RequireOwned(ContextTarget target, NativeMethods.Point point, bool requireCursor)
    {
        var before = GlassNativeDesktopProbe.ReadDesktop();
        var eligibility = GlassCaptureOwnership.CheckDesktop(before);
        Assert.True(eligibility.Allowed, "Native input desktop unavailable: " + eligibility.Reason);
        Assert.Equal(target.Handle.ToInt64(), before.Foreground);
        Assert.Equal((uint)Environment.ProcessId, before.ForegroundWindow!.ProcessId);
        Assert.Equal(target.Handle, BackdropNativeMethods.WindowFromPoint(new(point.X, point.Y)));
        if (requireCursor)
        {
            Assert.True(NativeMethods.GetCursorPos(out var cursor));
            Assert.Equal(point.X, cursor.X); Assert.Equal(point.Y, cursor.Y);
        }
        var after = GlassNativeDesktopProbe.ReadDesktop();
        Assert.True(GlassCaptureOwnership.CheckDesktop(after).Allowed);
        Assert.Equal(before.Foreground, after.Foreground);
        Assert.Equal(before.InputDesktop, after.InputDesktop);
    }

    private sealed class ContextTarget : Forms.NativeWindow, IDisposable
    {
        public int LeftDowns { get; private set; }
        public int LeftUps { get; private set; }
        public int Downs { get; private set; }
        public int Ups { get; private set; }
        public int ContextMenus { get; private set; }
        public ContextTarget() => CreateHandle(new Forms.CreateParams
        { Caption = "Citrus owned native cancellation proof", X = 80, Y = 80, Width = 340, Height = 240, Style = 0x10CF0000 });
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x0201) LeftDowns++;
            if (message.Msg == 0x0202) LeftUps++;
            if (message.Msg == 0x0204) Downs++;
            if (message.Msg == 0x0205) Ups++;
            if (message.Msg == 0x007B) { ContextMenus++; return; }
            base.WndProc(ref message); // DefWindowProc generates WM_CONTEXTMENU for an ordinary right UP.
        }
        public void Dispose() => DestroyHandle();
    }

    private sealed class UnknownSource : IDragSourceLifetime
    {
        public bool HasCompleted(bool leftReleased) => false;
        public void Dispose() { }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeMethods.Rect bounds);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativeMethods.Point point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extraInfo);
    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetCapture();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
}
