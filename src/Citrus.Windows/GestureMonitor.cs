using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Citrus.Core;

namespace Citrus.Windows;

/// <summary>Non-suppressing low-level observers, installed on the STA dispatcher with rooted callbacks.</summary>
public sealed class GestureMonitor : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly NativeMethods.HookProc keyboardCallback, mouseCallback;
    private readonly GestureState state;
    private readonly ModifierObserver modifiers;
    private long dragGeneration;
    private nint keyboardHook, mouseHook;
    private bool disposed;
    public event Action<OperationMode>? ModeRequested;
    public event Action? EscapePressed;
    public event Action? DragReleased;
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
                modifiers.Observe(key, true, Environment.TickCount64);
    }

    private void Dispatch(OperationMode? mode)
    {
        if (mode is { } value) dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!disposed && Enabled) ModeRequested?.Invoke(value); }));
    }

    private nint Keyboard(int code, nint message, nint data)
    {
        if (code >= 0 && !disposed)
        {
            var input = Marshal.PtrToStructure<NativeMethods.KeyboardData>(data);
            bool down = message == 0x100 || message == 0x104; // WM_KEYDOWN and WM_SYSKEYDOWN (Alt)
            if (down && input.Key == 0x1B) dispatcher.BeginInvoke(new Action(() => EscapePressed?.Invoke()));
            if (Enabled) Dispatch(modifiers.ObserveMessage((int)message, input.Key, input.Time));
        }
        return NativeMethods.CallNextHookEx(keyboardHook, code, message, data);
    }

    private nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0 && !disposed && Enabled)
        {
            var input = Marshal.PtrToStructure<NativeMethods.MouseData>(data);
            if (message == 0x201) { dragGeneration++; state.ButtonDown(input.Point.X, input.Point.Y); }
            else if (message == 0x200) Dispatch(state.Move(input.Point.X, input.Point.Y));
            else if (message == 0x202)
            {
                state.ButtonUp();
                // ContextIdle leaves OLE's pending Drop callback time to copy its plain paths first.
                long releasedGeneration = dragGeneration;
                dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { if (releasedGeneration == dragGeneration) DragReleased?.Invoke(); }));
            }
        }
        return NativeMethods.CallNextHookEx(mouseHook, code, message, data);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (keyboardHook != 0) NativeMethods.UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) NativeMethods.UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = 0;
    }
}
