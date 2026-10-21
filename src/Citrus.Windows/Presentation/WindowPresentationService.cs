using System.Windows.Interop;
using System.Windows.Media;
using Citrus.Core.Presentation;

namespace Citrus.Windows.Presentation;

/// <summary>Resident-owned services; future editor windows attach after their placement handlers are installed.</summary>
internal sealed class WindowPresentationService : IDisposable
{
    private readonly Application app;
    private readonly ThemeManager themes;
    private readonly MaterialPreferenceMonitor policy;
    private readonly Dictionary<Window, Attachment> windows = [];
    private bool disposed, monitoringMain;
    internal SpringAnimator Animator { get; } = new();
    internal WindowPresentationService(Application app, ThemeManager themes, Func<CitrusSettings> preferences)
    {
        this.app = app; this.themes = themes;
        policy = new(app.Dispatcher, () => new MaterialPreferences(preferences().ReduceTransparency, preferences().ReduceMotion));
        policy.Changed += RefreshWindows;
        themes.Changed += ThemeChanged;
        RefreshWindows();
    }
    internal void Attach(Window window, MaterialSurface? surface)
    {
        if (disposed || windows.ContainsKey(window)) return;
        PressFeedback.SetAnimator(window, Animator);
        var attachment = new Attachment(this, window, surface);
        windows.Add(window, attachment);
        attachment.Apply();
    }
    internal void Refresh() => policy.Refresh();
    private void ThemeChanged() => policy.RequestRefresh();
    private void RefreshWindows()
    {
        if (disposed) return;
        // Settle prior channels before changing typography/native surfaces. Sound is independently owned.
        Animator.MotionEnabled = policy.Decision.Animate;
        app.Resources["MotionEnabled"] = policy.Decision.Animate;
        themes.ApplyTypography(policy.TextScaleFactor);
        foreach (var attachment in windows.Values.ToArray()) attachment.Apply();
    }
    private static void CancelTree(DependencyObject root, SpringAnimator animator)
    {
        animator.CancelOwner(root, true);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) CancelTree(VisualTreeHelper.GetChild(root, i), animator);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        themes.Changed -= ThemeChanged;
        policy.Changed -= RefreshWindows;
        foreach (var attachment in windows.Values.ToArray()) attachment.Dispose();
        windows.Clear();
        policy.Dispose();
        Animator.Dispose();
    }
    private sealed class Attachment : IDisposable
    {
        private readonly WindowPresentationService owner;
        private readonly Window window;
        private readonly MaterialSurface? surface;
        private bool disposed;
        public Attachment(WindowPresentationService owner, Window window, MaterialSurface? surface)
        {
            this.owner = owner; this.window = window; this.surface = surface;
            window.SourceInitialized += Initialized;
            window.Closed += Closed;
            window.IsVisibleChanged += VisibilityChanged;
            if (new WindowInteropHelper(window).Handle != 0) Initialized(window, EventArgs.Empty);
        }
        private void Initialized(object? sender, EventArgs args)
        {
            // A quiet resident may not have created Home's HWND yet. Its persistent wheel can host
            // policy/session messages until Home exists; neither window is activated by attachment.
            if ((window is MainWindow || window is WheelWindow && !owner.monitoringMain) &&
                HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) is { } source)
            {
                owner.policy.Attach(source);
                if (window is MainWindow) owner.monitoringMain = true;
            }
            Apply();
        }
        private void ApplySolidBackground()
        {
            owner.themes.ApplyOpaqueChromePalette(window.Resources);
            surface?.ClearGlass();
            if (surface is not null && ReferenceEquals(window.Content, surface)) window.Background = Brushes.Transparent;
            else window.SetResourceReference(Window.BackgroundProperty, "WindowBackground");
        }
        internal void Apply()
        {
            if (disposed) return;
            if (window is WheelWindow wheel)
            {
                wheel.ApplyMaterialPolicy(owner.policy.Decision, owner.themes.IsDark, owner.policy.TextScaleFactor);
                return;
            }
            ApplySolidBackground();
            if (window is MainWindow home) home.ApplyTextScale(owner.policy.TextScaleFactor);
            if (window is WheelDemoWindow demo) demo.ApplyMaterialPolicy(owner.policy.Decision, owner.themes.IsDark);
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!window.IsVisible) CancelTree(window, owner.Animator);
        }
        private void Closed(object? sender, EventArgs args) => Dispose();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            window.SourceInitialized -= Initialized; window.Closed -= Closed; window.IsVisibleChanged -= VisibilityChanged;
            if (window is WheelWindow wheel) wheel.ReleasePresentation();
            CancelTree(window, owner.Animator);
            PressFeedback.SetAnimator(window, null);
            owner.windows.Remove(window);
        }
    }
}
