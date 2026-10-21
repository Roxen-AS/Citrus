using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

/// <summary>Read-only desktop/input identity. Never switches desktop, activates windows, or changes OS preferences.</summary>
internal static class GlassNativeDesktopProbe
{
    internal static GlassCaptureSnapshot AddRefusalIdentities(GlassCaptureSnapshot scene,
        Func<nint, GlassWindowIdentity>? describe = null)
    {
        const int limit = 32;
        describe ??= Describe;
        var windows = scene.Windows.ToArray();
        int attempted = 0;
        bool truncated = false;
        // These are diagnostics after refusal, never ownership evidence. Inspect only
        // visible intersecting windows above the owned background, with a fixed limit.
        for (int index = 0; index < windows.Length; index++)
        {
            var window = windows[index];
            if (window.Handle == scene.Background) break;
            if (!window.Visible || (window.BoundsKnown && !(window.Bounds.Left < scene.Crop.Right &&
                window.Bounds.Right > scene.Crop.Left && window.Bounds.Top < scene.Crop.Bottom &&
                window.Bounds.Bottom > scene.Crop.Top))) continue;
            if (attempted == limit) { truncated = true; continue; }
            attempted++;
            try { windows[index] = window with { Identity = describe(new(window.Handle)), IdentityReadFailure = null }; }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                windows[index] = window with { Identity = null, IdentityReadFailure = error.GetType().Name };
            }
        }
        return scene with { Windows = windows, RefusalIdentityLimit = limit, RefusalIdentitiesTruncated = truncated };
    }

    internal static GlassCaptureSnapshot Complete(GlassCaptureSnapshot scene)
    {
        var desktop = ReadDesktop();
        // Desktop identity is immutable within this snapshot. Keep the full physical-pixel
        // ownership walk, without allocating/checking the same eligibility result per pixel.
        var initialDesktop = GlassCaptureOwnership.CheckDesktop(desktop);
        var bounds = scene.Crop;
        long required = (long)(bounds.Width * bounds.Height), checkedPixels = 0;
        bool ownedDragCapture = scene.DragCapture != 0 && scene.DragCaptureIdentity is { } capture &&
            capture.Handle == scene.DragCapture && capture.RootHandle == scene.DragCapture && capture.ProcessId == Environment.ProcessId;
        if (required <= 0 || required > 16_777_216)
            return scene with { Desktop = desktop, Input = new(0, required, false, Failure: "UnboundedInputCrop") };
        // Always record a first input mismatch, including when desktop identity already refused capability.
        for (int y = (int)bounds.Top; y < (int)bounds.Bottom; y++)
            for (int x = (int)bounds.Left; x < (int)bounds.Right; x++)
            {
                nint hit = BackdropNativeMethods.WindowFromPoint(new(x, y));
                nint root = hit == 0 ? 0 : GetAncestor(hit, 2); // GA_ROOT includes child HWNDs of owned windows.
                checkedPixels++;
                if (root.ToInt64() != scene.Wheel && root.ToInt64() != scene.Background &&
                    (!ownedDragCapture || root.ToInt64() != scene.DragCapture))
                    return scene with
                    {
                        Desktop = desktop, Input = new(checkedPixels, required, false, x, y, root.ToInt64(), "UnownedInputRoot"),
                        InputMismatchWindow = Describe(hit)
                    };
                if (!initialDesktop.Allowed)
                    return scene with { Desktop = desktop, Input = new(checkedPixels, required, false, x, y, root.ToInt64(), "InputDesktopUnavailable") };
            }
        var finalDesktop = ReadDesktop();
        var transition = EvaluateDesktopTransition(desktop, finalDesktop);
        return scene with
        {
            Desktop = finalDesktop with { QueryDiagnostic = transition.Diagnostic },
            Input = new(checkedPixels, required, transition.Stable, Failure: transition.Stable ? null : "DesktopOrForegroundChangedDuringPreflight")
        };
    }

    internal static (bool Stable, string Diagnostic) EvaluateDesktopTransition(GlassDesktopSnapshot initial, GlassDesktopSnapshot final)
    {
        var finalEligibility = GlassCaptureOwnership.CheckDesktop(final);
        bool foregroundStable = final.Foreground == initial.Foreground;
        bool threadDesktopStable = final.ThreadDesktop == initial.ThreadDesktop;
        bool inputDesktopStable = final.InputDesktop == initial.InputDesktop;
        bool processSessionStable = final.ProcessSession == initial.ProcessSession;
        // Preserve the exact existing identity policy. OpenInputDesktop returns
        // independently owned handles; their numeric values remain diagnostics.
        bool stable = finalEligibility.Allowed && foregroundStable && threadDesktopStable && inputDesktopStable && processSessionStable;
        string diagnostic = JsonSerializer.Serialize(new
        {
            Initial = initial, Final = final,
            InitialEligibility = GlassCaptureOwnership.CheckDesktop(initial), FinalEligibility = finalEligibility,
            ForegroundStable = foregroundStable, ThreadDesktopStable = threadDesktopStable,
            InputDesktopStable = inputDesktopStable, ProcessSessionStable = processSessionStable
        });
        return (stable, diagnostic);
    }

    internal static GlassDesktopSnapshot ReadDesktop()
    {
        nint thread = GetThreadDesktop(GetCurrentThreadId()), station = GetProcessWindowStation();
        nint input = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS, no switch/write access.
        int error = input == 0 ? Marshal.GetLastWin32Error() : 0;
        try
        {
            string threadName = Name(thread), inputName = Name(input), stationName = Name(station);
            int receivesInput = 0;
            bool inputFlagKnown = thread != 0 && GetUserObjectInt(thread, 6, out receivesInput, 4, out _);
            bool threadIsInput = inputFlagKnown && receivesInput != 0;
            bool sessionKnown = ProcessIdToSessionId((uint)Environment.ProcessId, out uint session);
            bool wtsKnown = WTSQuerySessionInformation(0, session, 8, out nint data, out uint bytes);
            int state = -1;
            try { if (wtsKnown && bytes >= 4) state = Marshal.ReadInt32(data); }
            finally { if (data != 0) WTSFreeMemory(data); }
            nint foreground = BackdropNativeMethods.GetForegroundWindow();
            var identity = Describe(foreground);
            bool known = input != 0 && threadName.Length > 0 && inputName.Length > 0 && stationName.Length > 0 &&
                inputFlagKnown && sessionKnown && wtsKnown && state >= 0;
            string queries = $"inputOpened={input != 0};threadNameRead={threadName.Length > 0};inputNameRead={inputName.Length > 0};stationNameRead={stationName.Length > 0};threadInputFlagRead={inputFlagKnown};sessionRead={sessionKnown};wtsStateRead={wtsKnown && state >= 0}";
            return new(Environment.UserInteractive, known, stationName, threadName, inputName, threadIsInput,
                session, WTSGetActiveConsoleSessionId(), state, foreground.ToInt64(), identity.SessionId,
                thread.ToInt64(), input.ToInt64(), station.ToInt64(), error, identity, queries);
        }
        finally { if (input != 0) CloseDesktop(input); }
    }

    internal static GlassWindowIdentity Describe(nint hwnd)
    {
        uint thread = GetWindowThreadProcessId(hwnd, out uint process);
        uint? session = ProcessIdToSessionId(process, out uint value) ? value : null;
        var className = new StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);
        string processName = "Unavailable";
        DateTime? processStartedUtc = null;
        try
        {
            using var owner = Process.GetProcessById((int)process);
            processName = owner.ProcessName;
            try { processStartedUtc = owner.StartTime.ToUniversalTime(); }
            catch (Exception) { /* Start time can be unavailable even when the name was readable. */ }
        }
        catch (Exception) { /* Access/exit races are diagnostic unknowns, never evidence of ownership. */ }
        bool known = BackdropNativeMethods.GetWindowRect(hwnd, out var bounds);
        return new(hwnd.ToInt64(), GetAncestor(hwnd, 2).ToInt64(), className.ToString(), process, processName, thread,
            session, known, bounds.ToScreenRect(), BackdropNativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64(),
            BackdropNativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64(), Name(GetThreadDesktop(thread)), processStartedUtc);
    }
    private static string Name(nint handle)
    {
        if (handle == 0) return "";
        var name = new StringBuilder(256);
        return GetUserObjectName(handle, 2, name, name.Capacity * sizeof(char), out _) ? name.ToString() : "";
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint process, out uint session);
    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32.dll")] private static extern nint GetProcessWindowStation();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectName(nint handle, int index, StringBuilder information, int length, out int needed);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    private static extern bool GetUserObjectInt(nint handle, int index, out int information, int length, out int needed);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(nint server, uint session, int information, out nint data, out uint bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint memory);
}
