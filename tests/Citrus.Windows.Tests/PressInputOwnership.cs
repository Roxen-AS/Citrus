namespace Citrus.Windows.Tests;

internal sealed record PressInputSnapshot(GlassDesktopSnapshot Before, GlassDesktopSnapshot After,
    long HostHandle, uint ProcessId, GlassWindowIdentity Host, GlassWindowIdentity Hit,
    bool HostVisible, bool HostEnabled, bool WpfSourceMatches, bool WpfTargetMatches,
    bool CursorMatches, bool CaptureKnown, GlassWindowIdentity? Capture);

/// <summary>Test-only admission for a single global pointer operation; no native calls or input side effects.</summary>
internal static class PressInputOwnership
{
    internal static GlassCapturePreflight Check(PressInputSnapshot snapshot)
    {
        var before = GlassCaptureOwnership.CheckDesktop(snapshot.Before);
        if (!before.Allowed) return before;
        var after = GlassCaptureOwnership.CheckDesktop(snapshot.After);
        if (!after.Allowed) return after;
        if (snapshot.Before.Foreground != snapshot.After.Foreground ||
            snapshot.Before.ThreadDesktopHandle != snapshot.After.ThreadDesktopHandle ||
            snapshot.Before.WindowStationHandle != snapshot.After.WindowStationHandle ||
            snapshot.Before.ProcessSession != snapshot.After.ProcessSession ||
            snapshot.Before.ActiveConsoleSession != snapshot.After.ActiveConsoleSession ||
            snapshot.Before.ThreadDesktop != snapshot.After.ThreadDesktop ||
            snapshot.Before.InputDesktop != snapshot.After.InputDesktop)
            return new(false, "DesktopOrForegroundChangedDuringPointerPreflight");
        bool Owned(GlassWindowIdentity? window) => window is not null && snapshot.HostHandle != 0 &&
            window.RootHandle == snapshot.HostHandle && window.ProcessId == snapshot.ProcessId &&
            window.SessionId == snapshot.Before.ProcessSession &&
            string.Equals(window.DesktopName, snapshot.Before.InputDesktop, StringComparison.OrdinalIgnoreCase);
        if (!Owned(snapshot.Host) || snapshot.Host.Handle != snapshot.HostHandle ||
            !snapshot.HostVisible || !snapshot.HostEnabled)
            return new(false, "ExpectedOwnedHostUnavailable");
        if (!Owned(snapshot.Before.ForegroundWindow) || !Owned(snapshot.After.ForegroundWindow))
            return new(false, "ForegroundIsNotExpectedOwnedHost");
        if (!Owned(snapshot.Hit)) return new(false, "PointerTargetIsNotExpectedOwnedHost");
        if (!snapshot.WpfSourceMatches || !snapshot.WpfTargetMatches)
            return new(false, "ActualWpfSourceOrButtonHitMismatch");
        if (!snapshot.CursorMatches) return new(false, "CursorMovedBeforePointerInjection");
        if (!snapshot.CaptureKnown || (snapshot.Capture is not null && !Owned(snapshot.Capture)))
            return new(false, "PointerCaptureIsUnknownOrUnowned");
        return new(true, "ActualOwnedPointerTargetBeforeOperation");
    }
}
