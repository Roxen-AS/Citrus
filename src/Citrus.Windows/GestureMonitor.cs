using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Citrus.Core;

namespace Citrus.Windows;

/// <summary>Low-level gesture observers; only an explicitly claimed wheel cancellation pair is suppressed.</summary>
public sealed class GestureMonitor : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly NativeMethods.HookProc keyboardCallback, mouseCallback;
    private readonly GestureState state;
    private readonly ModifierObserver modifiers;
    private nint keyboardHook, mouseHook;
    private bool disposed, rightButtonOwned;
    public event Action<OperationMode>? ModeRequested;
    public event Action? EscapePressed;
    /// <summary>Hook-thread notification: invalidate drop admission before returning to OLE.</summary>
    public event Action? CancellationObserved;
    /// <summary>Returns true only when an admitted wheel owns this right-button pair.</summary>
    public event Func<bool>? RightButtonCancellationRequested;
    private bool ClaimRightCancellation() => RightButtonCancellationRequested?.Invoke() == true;
    /// <summary>Observer notification on the hook thread; handlers only snapshot session identity and queue work.</summary>
    public event Action? DragReleased;
    /// <summary>Observed on the hook thread, including during Explorer's modal OLE drag.</summary>
    public event Action<int>? WheelScrolled;
    public bool Enabled { get; private set; } = true;

    public GestureMonitor(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        // The system drag rectangle extends half its width/height in each direction.
        state = new(Math.Max(1, NativeMethods.GetSystemMetrics(68) / 2.0), Math.Max(1, NativeMethods.GetSystemMetrics(69) / 2.0));
        modifiers = new(state);
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
        SeedModifiers();
        keyboardHook = NativeMethods.SetWindowsHookEx(13, keyboardCallback, NativeMethods.GetModuleHandle(null), 0);
        mouseHook = NativeMethods.SetWindowsHookEx(14, mouseCallback, NativeMethods.GetModuleHandle(null), 0);
        if (keyboardHook == 0 || mouseHook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Citrus could not start gesture observation.");
        }
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        state.ButtonUp();
        if (enabled) SeedModifiers();
    }

    private void SeedModifiers()
    {
        modifiers.Reset();
        // Initial snapshot is outside the keyboard callback; callbacks themselves use event state only.
        foreach (uint key in new uint[] { 0xA0, 0xA1, 0xA4, 0xA5, 0xA2, 0xA3 })
            if ((NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0)
                modifiers.Seed(key);
    }

    private void Dispatch(OperationMode? mode)
    {
        if (mode is not { } value) return;
        long epoch = state.Epoch;
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!disposed && Enabled && state.Epoch == epoch) ModeRequested?.Invoke(value);
        }));
    }

    private void DispatchCancellation()
    {
        state.ButtonUp(); // Invalidate queued activation before posting cancellation.
        CancellationObserved?.Invoke();
        // Same priority as mode requests preserves cancellation-before-next-activation ordering.
        // A subsequent button-up must not discard Escape for an already confirmed OLE session.
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!disposed) EscapePressed?.Invoke();
        }));
    }

    private nint Keyboard(int code, nint message, nint data)
    {
        if (code >= 0 && !disposed)
        {
            var input = Marshal.PtrToStructure<NativeMethods.KeyboardData>(data);
            bool down = message == 0x100 || message == 0x104; // WM_KEYDOWN and WM_SYSKEYDOWN (Alt)
            if (down && input.Key == 0x1B) DispatchCancellation();
            if (Enabled) Dispatch(modifiers.ObserveMessage((int)message, input.Key, Environment.TickCount64));
        }
        return NativeMethods.CallNextHookEx(keyboardHook, code, message, data);
    }

    private nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0 && !disposed)
        {
            // Pair ownership survives hiding or pausing the wheel. A repeated DOWN belongs to the
            // existing pair and must never ask a replacement wheel to cancel.
            if (message == 0x0205 && rightButtonOwned)
            { rightButtonOwned = false; return 1; }
            if (message == 0x0204)
            {
                if (rightButtonOwned) return 1;
                if (Enabled && ClaimRightCancellation())
                { rightButtonOwned = true; state.ButtonUp(); return 1; }
            }
            if (message == 0x0202)
            {
                state.ButtonUp();
                // Draining an already admitted drag still needs its release observation while paused.
                DragReleased?.Invoke();
            }
            if (!Enabled) return NativeMethods.CallNextHookEx(mouseHook, code, message, data);
            var input = Marshal.PtrToStructure<NativeMethods.MouseData>(data);
            if (message == 0x201) state.ButtonDown(input.Point.X, input.Point.Y);
            else if (message == 0x200) Dispatch(state.Move(input.Point.X, input.Point.Y, Environment.TickCount64));
            else if (message == 0x020A) WheelScrolled?.Invoke(unchecked((short)(input.Mouse >> 16)));
        }
        return NativeMethods.CallNextHookEx(mouseHook, code, message, data);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        state.ButtonUp();
        if (keyboardHook != 0) NativeMethods.UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) NativeMethods.UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = 0;
    }
}
