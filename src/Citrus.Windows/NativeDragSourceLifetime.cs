using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Citrus.Windows;

/// <summary>Retains kernel identities rather than reopening a potentially reused PID or thread ID.</summary>
internal sealed class NativeDragSourceLifetime : IDragSourceLifetime
{
    private readonly SafeWaitHandle? process, thread;
    private readonly uint threadId;
    private readonly nint originalCapture;
    private bool disposed;

    private NativeDragSourceLifetime(SafeWaitHandle? process = null, SafeWaitHandle? thread = null,
        uint threadId = 0, nint originalCapture = 0)
    { this.process = process; this.thread = thread; this.threadId = threadId; this.originalCapture = originalCapture; }

    internal static IDragSourceLifetime Capture()
    {
        var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        // The OLE capture window identifies the source. Foreground alone is not evidence: another
        // app can take foreground while a drag is active, so an unknown capture stays unknown.
        if (!GetGUIThreadInfo(0, ref gui) || gui.Capture == 0) return new NativeDragSourceLifetime();
        nint sourceCapture = gui.Capture;
        uint sourceThread = GetWindowThreadProcessId(sourceCapture, out uint sourceProcess);
        if (sourceThread == 0 || sourceProcess == 0) return new NativeDragSourceLifetime();
        var process = OpenProcess(0x00100000 | 0x1000, false, sourceProcess); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        var thread = OpenThread(0x00100000 | 0x0800, false, sourceThread);
        // Validate retained objects against the original window after acquiring both handles.
        if (process.IsInvalid || thread.IsInvalid || GetProcessId(process) != sourceProcess ||
            GetThreadId(thread) != sourceThread || GetProcessIdOfThread(thread) != sourceProcess ||
            GetWindowThreadProcessId(sourceCapture, out uint currentProcess) != sourceThread || currentProcess != sourceProcess)
        { process.Dispose(); thread.Dispose(); return new NativeDragSourceLifetime(); }
        gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        // If capture moved while identity was sampled, retain only exit evidence.
        nint capture = GetGUIThreadInfo(sourceThread, ref gui) && gui.Capture == sourceCapture ? sourceCapture : 0;
        return new NativeDragSourceLifetime(process, thread, sourceThread, capture);
    }

    public bool HasCompleted(bool leftReleased)
    {
        if (disposed) return false;
        if (Signalled(process) || Signalled(thread)) return true;
        if (!leftReleased || originalCapture == 0 || thread is null || thread.IsInvalid || thread.IsClosed) return false;
        // A real release is sufficient; a desktop-sized target need not receive Leave.
        // No HWND/PID is reopened.
        // A failed query or an extant capture is inconclusive and keeps the drain target alive.
        var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref gui) && gui.Capture == 0;
    }

    private static bool Signalled(SafeWaitHandle? identity) =>
        identity is { IsInvalid: false, IsClosed: false } && WaitForSingleObject(identity, 0) == 0;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; process?.Dispose(); thread?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public NativeMethods.Rect CaretBounds;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo information);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeWaitHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeWaitHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);
    [DllImport("kernel32.dll")]
    private static extern uint GetProcessId(SafeWaitHandle process);
    [DllImport("kernel32.dll")]
    private static extern uint GetThreadId(SafeWaitHandle thread);
    [DllImport("kernel32.dll")]
    private static extern uint GetProcessIdOfThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
