using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Citrus.Core.Presentation;
using Windows.UI.ViewManagement;

namespace Citrus.Windows.Presentation;

/// <summary>Documented SDK WinRT projection, marshaled onto one dispatcher; no polling or registry substitute.</summary>
internal sealed class MaterialPreferenceMonitor : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Func<MaterialPreferences> preferences;
    private UISettings? settings;
    private HwndSource? source;
    private DispatcherOperation? pending;
    private bool disposed;
    public MaterialDecision Decision { get; private set; }
    public string PreferenceDiagnostic { get; private set; } = "NotRead";
    public double TextScaleFactor { get; private set; } = 1;
    public event Action? Changed;

    public MaterialPreferenceMonitor(Dispatcher dispatcher, Func<MaterialPreferences> preferences)
    {
        this.dispatcher = dispatcher; this.preferences = preferences;
        dispatcher.VerifyAccess();
        try
        {
            settings = new UISettings();
            settings.AdvancedEffectsEnabledChanged += EffectsChanged;
            settings.TextScaleFactorChanged += TextChanged;
        }
        catch (Exception) { ReleaseSettings(); }
        SystemParameters.StaticPropertyChanged += SystemChanged;
        Refresh();
    }
    public void Attach(HwndSource source)
    {
        dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (this.source is not null) { this.source.RemoveHook(Hook); BackdropNativeMethods.WTSUnRegisterSessionNotification(this.source.Handle); }
        this.source = source; source.AddHook(Hook);
        BackdropNativeMethods.WTSRegisterSessionNotification(source.Handle, 0);
    }
    public void RequestRefresh()
    {
        if (dispatcher.CheckAccess()) QueueRefresh();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(QueueRefresh));
    }
    private void QueueRefresh()
    {
        if (disposed || pending?.Status == DispatcherOperationStatus.Pending) return;
        pending = dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => { pending = null; if (!disposed) Refresh(); }));
    }
    public void Refresh()
    {
        dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        Decision = MaterialPolicy.Resolve(preferences(), Read());
        Changed?.Invoke();
    }
    public MaterialEnvironment Read()
    {
        dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        bool effects = false;
        double scale = 1;
        PreferenceDiagnostic = "WinRtUnavailable";
        try
        {
            if (settings is not null)
            {
                effects = settings.AdvancedEffectsEnabled;
                scale = settings.TextScaleFactor;
                PreferenceDiagnostic = effects ? "Available" : "AdvancedEffectsDisabled";
            }
        }
        catch (Exception) { effects = false; PreferenceDiagnostic = "WinRtUnavailable"; }
        bool composition = BackdropNativeMethods.DwmIsCompositionEnabled(out bool enabled) >= 0 && enabled;
        TextScaleFactor = double.IsFinite(scale) && scale >= 1 ? scale : 1;
        return new(Environment.OSVersion.Version.Build, composition, effects, SystemParameters.HighContrast,
            SystemParameters.IsRemoteSession, SystemParameters.ClientAreaAnimation);
    }
    private void EffectsChanged(UISettings sender, object args) => RequestRefresh();
    private void TextChanged(UISettings sender, object args) => RequestRefresh();
    private void SystemChanged(object? sender, PropertyChangedEventArgs args) => RequestRefresh();
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x31E or 0x1A or 0x31A or 0x2B1) RequestRefresh();
        return 0;
    }
    private void ReleaseSettings()
    {
        if (settings is null) return;
        try { settings.AdvancedEffectsEnabledChanged -= EffectsChanged; }
        catch (Exception) { /* A disconnected WinRT server still resolves to Solid. */ }
        try { settings.TextScaleFactorChanged -= TextChanged; }
        catch (Exception) { /* Attempt both removals even if one interface disconnected. */ }
        settings = null;
    }
    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true; pending?.Abort(); pending = null;
        SystemParameters.StaticPropertyChanged -= SystemChanged;
        if (source is not null) { source.RemoveHook(Hook); BackdropNativeMethods.WTSUnRegisterSessionNotification(source.Handle); source = null; }
        ReleaseSettings(); Changed = null;
    }
}
