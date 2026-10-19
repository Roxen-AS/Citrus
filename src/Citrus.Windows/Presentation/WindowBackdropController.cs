using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Citrus.Core.Presentation;

namespace Citrus.Windows.Presentation;

internal sealed class WindowBackdropController : IDisposable
{
    private readonly IBackdropApi api;
    private Window? window;
    private HwndSource? source;
    private Color originalBackground;
    private bool disposed;
    private MaterialDecision decision;
    private bool dark;
    public BackdropResult Result { get; private set; }
    public event Action? Invalidated;
    public WindowBackdropController() : this(new DwmBackdropApi()) { }
    internal WindowBackdropController(IBackdropApi api) => this.api = api;

    public void Attach(Window window)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        window.Dispatcher.VerifyAccess();
        if (this.window is not null) throw new InvalidOperationException("Already attached.");
        this.window = window;
        window.SourceInitialized += SourceInitialized;
        window.Closed += Closed;
        if (new WindowInteropHelper(window).Handle != 0) Connect();
    }
    private void SourceInitialized(object? sender, EventArgs e) => Connect();
    private void Connect()
    {
        if (source is not null || window is null) return;
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        if (source is null) return;
        originalBackground = source.CompositionTarget.BackgroundColor;
        source.AddHook(Hook);
        Apply(decision, dark);
    }
    public BackdropResult Apply(MaterialDecision decision, bool dark)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        window?.Dispatcher.VerifyAccess();
        this.decision = decision; this.dark = dark;
        if (source is null) return Result = BackdropResult.Solid(decision, "NotAttached");
        if ((BackdropNativeMethods.GetWindowLongPtr(source.Handle, -20).ToInt64() & BackdropNativeMethods.WsExLayered) != 0)
            return Result = BackdropResult.Solid(decision, "LayeredWindow");
        if (decision.RequestNativeAcrylic) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        Result = BackdropConfiguration.Apply(api, source.Handle, decision, dark);
        if (!Result.IsNative) source.CompositionTarget.BackgroundColor = originalBackground;
        return Result;
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x31E or 0x1A or 0x31A or 0x2B1)
        {
            // Remove the native effect synchronously, before the debounced policy re-query.
            BackdropConfiguration.Reset(api, hwnd);
            if (source is not null) source.CompositionTarget.BackgroundColor = originalBackground;
            Result = BackdropResult.Solid(decision, "EnvironmentChanged");
            Invalidated?.Invoke();
        }
        return 0;
    }
    private void Closed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (disposed) return;
        window?.Dispatcher.VerifyAccess();
        disposed = true;
        if (source is not null)
        {
            source.RemoveHook(Hook);
            if (!source.IsDisposed)
            {
                BackdropConfiguration.Reset(api, source.Handle);
                source.CompositionTarget.BackgroundColor = originalBackground;
            }
            source = null;
        }
        if (window is not null) { window.SourceInitialized -= SourceInitialized; window.Closed -= Closed; window = null; }
        Invalidated = null;
    }
}
