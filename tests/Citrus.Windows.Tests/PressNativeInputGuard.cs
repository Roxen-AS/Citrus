using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

/// <summary>
/// Test-only boundary for genuine global pointer input. Every operation gets a fresh read-only preflight.
/// Windows does not provide an atomic ownership-check-and-inject API: another actor can change the desktop
/// after the last read. This minimizes that TOCTOU window; it cannot prove isolation from concurrent actors.
/// </summary>
internal sealed class PressNativeInputGuard(Window host, System.Windows.Controls.Button button) : IDisposable
{
    private bool downSent;

    internal readonly record struct Target(int X, int Y, bool? ButtonHit)
    {
        internal static Target At(System.Windows.Point point, bool buttonHit) =>
            new(checked((int)Math.Round(point.X)), checked((int)Math.Round(point.Y)), buttonHit);
    }

    internal void RequireTargets(params Target[] targets)
    {
        foreach (var target in targets) Require(target, "initial target preflight", requireCursor: false);
    }

    internal void MoveTo(Target target)
    {
        if (!downSent) RequireNoExistingPress();
        Require(target, "cursor movement", requireCursor: false);
        Assert.True(SetCursorPos(target.X, target.Y), "SetCursorPos failed for the admitted owned target.");
    }

    internal void Down(Target target)
    {
        Assert.False(downSent);
        RequireNoExistingPress();
        Require(target, "LEFTDOWN", requireCursor: true);
        MouseEvent(0x0002, 0, 0, 0, UIntPtr.Zero);
        downSent = true;
    }

    internal void Up(Target target)
    {
        Assert.True(downSent, "This test must emit DOWN before it may emit UP.");
        Require(target, "LEFTUP", requireCursor: true);
        MouseEvent(0x0004, 0, 0, 0, UIntPtr.Zero);
        downSent = false;
    }

    public void Dispose()
    {
        if (!downSent) return; // A refused preflight must never cause an unsolicited global LEFTUP.
        try
        {
            Assert.True(GetCursorPos(out var cursor), "Pointer cleanup capability unavailable: cursor query failed; no LEFTUP emitted.");
            Up(new(cursor.X, cursor.Y, null)); // Release only while this actual cursor point remains owned.
        }
        finally
        {
            // Managed release only. If ownership was lost, never inject into the new foreground/window.
            button.ReleaseMouseCapture();
        }
        // Do not restore the original global cursor: that destination may belong to unrelated UI.
    }

    private void Require(Target target, string operation, bool requireCursor)
    {
        host.Dispatcher.VerifyAccess();
        long handle = new WindowInteropHelper(host).Handle.ToInt64();
        var before = GlassNativeDesktopProbe.ReadDesktop();
        var hostIdentity = GlassNativeDesktopProbe.Describe((nint)handle);
        var hit = GlassNativeDesktopProbe.Describe(BackdropNativeMethods.WindowFromPoint(new(target.X, target.Y)));
        var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        bool captureKnown = before.ForegroundWindow is { ThreadId: not 0 } foreground &&
            GetGUIThreadInfo(foreground.ThreadId, ref gui);
        var capture = gui.Capture == 0 ? null : GlassNativeDesktopProbe.Describe(gui.Capture);
        bool sourceMatches = PresentationSource.FromVisual(button) is HwndSource source && source.Handle.ToInt64() == handle &&
            host.IsVisible && button.IsVisible && button.IsEnabled;
        var element = host.InputHitTest(host.PointFromScreen(new(target.X, target.Y))) as DependencyObject;
        bool buttonHit = element is not null && (ReferenceEquals(element, button) || button.IsAncestorOf(element));
        bool cursorMatches = !requireCursor || (GetCursorPos(out var cursor) && cursor.X == target.X && cursor.Y == target.Y);
        var after = GlassNativeDesktopProbe.ReadDesktop();
        var result = PressInputOwnership.Check(new(before, after, handle, (uint)Environment.ProcessId,
            hostIdentity, hit, BackdropNativeMethods.IsWindow((nint)handle) && BackdropNativeMethods.IsWindowVisible((nint)handle),
            IsWindowEnabled((nint)handle), sourceMatches, target.ButtonHit is null || target.ButtonHit == buttonHit,
            cursorMatches, captureKnown, capture));
        Assert.True(result.Allowed,
            $"Native pointer capability unavailable before {operation}: {result.Reason}; " +
            $"point=({target.X},{target.Y}), expectedHost={handle}/PID{Environment.ProcessId}, " +
            $"hit={hit.Handle}/root{hit.RootHandle}/PID{hit.ProcessId}, " +
            $"foreground={after.ForegroundWindow?.Handle}/PID{after.ForegroundWindow?.ProcessId}. " +
            "No global input was emitted by this refused operation.");
    }

    private static void RequireNoExistingPress() => Assert.True((GetAsyncKeyState(1) & 0x8000) == 0,
        "Native pointer capability unavailable: LEFT is already down; no global input emitted.");

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint handle);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo information);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
