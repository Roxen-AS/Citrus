using System;
using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Core.Presentation;

namespace Citrus.Windows.Presentation;

internal sealed class WheelBackdropController : IDisposable
{
    private readonly bool nativeCompanionValidated;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly IBackdropApi api = new DwmBackdropApi();
    private HwndSource? source;
    private NativeBackdropWindow? companion;
    private long generation = long.MinValue, cancelledThrough = long.MinValue;
    private bool disposed, positioning;
    private ScreenRect placedBounds;
    private double placedScale;
    private bool placedDark;
    public nint CompanionHandle => companion?.Handle ?? 0;
    public nint LifetimeOwnerHandle => companion?.Owner ?? 0;
    public bool IsAttached => source is not null;
    public BackdropResult Result { get; private set; }
    // Subscriber must re-read actual HWND placement/policy and wait for complete WPF presentation.
    public event Action? Invalidated;
    public WheelBackdropController(bool nativeCompanionValidated = false) => this.nativeCompanionValidated = nativeCompanionValidated;

    public void Attach(nint wheelHwnd)
    {
        Verify();
        if (source is not null) throw new InvalidOperationException("Already attached.");
        source = HwndSource.FromHwnd(wheelHwnd) ?? throw new ArgumentException("A live WPF wheel HWND is required.", nameof(wheelHwnd));
        if ((BackdropNativeMethods.GetWindowLongPtr(wheelHwnd, -20).ToInt64() & BackdropNativeMethods.WsExLayered) == 0)
        { source = null; throw new ArgumentException("The real wheel must retain its layered HWND.", nameof(wheelHwnd)); }
        source.AddHook(Hook);
        source.Disposed += SourceDisposed;
    }

    public BackdropResult Synchronize(long generation, ScreenRect wheelPhysicalBounds, double dpiScale,
        bool confirmedVisible, MaterialDecision decision, bool dark)
    {
        Verify();
        if (generation < this.generation || generation <= cancelledThrough) return BackdropResult.Solid(decision, "StaleGeneration");
        if (generation > this.generation) { companion?.Hide(); this.generation = generation; }
        if (!confirmedVisible) { companion?.Hide(); return Result = BackdropResult.Solid(decision, "UnconfirmedOrNotPresented"); }
        if (!nativeCompanionValidated || !decision.RequestNativeAcrylic)
        {
            RemoveCompanion();
            return Result = BackdropResult.Solid(decision, !decision.RequestNativeAcrylic ? decision.Reason : "CompanionNotValidated");
        }
        if (source is null || source.IsDisposed || !BackdropNativeMethods.IsWindowVisible(source.Handle))
        { RemoveCompanion(); return Result = BackdropResult.Solid(decision, "WheelNotVisible"); }
        if (!double.IsFinite(dpiScale) || dpiScale <= 0 ||
            Math.Abs(dpiScale - BackdropNativeMethods.GetDpiForWindow(source.Handle) / 96d) > .001 ||
            !BackdropNativeMethods.GetWindowRect(source.Handle, out var actual) || actual.ToScreenRect() != wheelPhysicalBounds)
        { companion?.Hide(); return Result = BackdropResult.Solid(decision, "PlacementMismatch"); }
        if (companion is not null && BackdropNativeMethods.IsWindowVisible(companion.Handle) &&
            placedBounds == wheelPhysicalBounds && placedScale == dpiScale && placedDark == dark) return Result;
        try
        {
            positioning = true;
            companion ??= new NativeBackdropWindow();
            companion.Hide();
            Result = BackdropConfiguration.Apply(api, companion.Handle, decision, dark);
            if (!Result.IsNative) { RemoveCompanion(); return Result; }
            companion.PlaceBehind(source.Handle, wheelPhysicalBounds, dpiScale);
            placedBounds = wheelPhysicalBounds; placedScale = dpiScale; placedDark = dark;
            return Result;
        }
        catch (Exception exception) when (exception is Win32Exception or ArgumentOutOfRangeException)
        {
            RemoveCompanion();
            return Result = BackdropResult.Solid(decision, "NativePlacementFailed", exception.HResult);
        }
        finally { positioning = false; }
    }
    public void Hide(long generation)
    {
        if (disposed) return;
        dispatcher.VerifyAccess();
        if (generation < this.generation) return;
        this.generation = generation;
        cancelledThrough = Math.Max(cancelledThrough, generation);
        companion?.Hide();
        Result = Result with { State = BackdropState.Solid, Reason = "Hidden" };
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (positioning) return 0;
        if (message == 0x82) { Dispose(); return 0; } // WM_NCDESTROY
        if (message == 0x18 && wParam == 0) { Hide(generation); return 0; }
        if (message is 0x46 or 0x47 or 0x2E0 or 0x31E or 0x1A or 0x31A or 0x2B1)
        {
            companion?.Hide();
            Result = Result with { State = BackdropState.Solid, Reason = "EnvironmentOrPlacementChanged" };
            Invalidated?.Invoke();
        }
        return 0;
    }
    private void SourceDisposed(object? sender, EventArgs e) => Dispose();
    private void RemoveCompanion() { companion?.Dispose(); companion = null; }
    private void Verify() { dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this); }
    public void Dispose()
    {
        if (disposed) return;
        dispatcher.VerifyAccess(); disposed = true;
        RemoveCompanion();
        if (source is not null) { source.RemoveHook(Hook); source.Disposed -= SourceDisposed; source = null; }
        Invalidated = null;
    }
}
