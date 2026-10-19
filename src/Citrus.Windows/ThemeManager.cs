using System.ComponentModel;
using System.Windows.Media;
using Microsoft.Win32;

namespace Citrus.Windows;

internal sealed class ThemeManager : IDisposable
{
    private readonly Application app;
    private ThemeChoice choice;
    private bool reduceMotion, disposed;
    internal bool IsDark { get; private set; }
    internal event Action? Changed;
    public ThemeManager(Application app)
    {
        this.app = app;
        SystemParameters.StaticPropertyChanged += ParametersChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
    }
    private void ParametersChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Refresh();
    private void Refresh() => app.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) Apply(choice, reduceMotion); }));
    public void Apply(ThemeChoice selected, bool reduceMotion = false) => ApplyPalette(selected, reduceMotion, SystemParameters.HighContrast);
    internal void ApplyPalette(ThemeChoice selected, bool reduceMotion, bool highContrast)
    {
        choice = selected;
        this.reduceMotion = reduceMotion;
        bool light = selected == ThemeChoice.Light;
        if (selected == ThemeChoice.System)
        {
            using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (settings?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        IsDark = !light;
        var colors = light
            ? new[] { "#F7F5F2", "#FFFFFF", "#202020", "#62605D", "#A42615", "#C14D00", "#FFFFFF", "#FFFFFF", "#202428" }
            : new[] { "#24211F", "#312E2A", "#F8F5F0", "#CBC3B9", "#FFB49C", "#FFAD6C", "#241408", "#302D29", "#F3EFE9" };
        string[] keys = ["WindowBackground", "PanelBackground", "PrimaryText", "SecondaryText", "ErrorText", "Accent", "AccentText", "ControlBackground", "ControlText"];
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        string[] extraKeys = ["InsetBackground", "BorderBrush", "ActionInk", "WheelShell", "PetalBackground", "SelectedPetalText"];
        string[] extraColors = light ? ["#F0EFEC", "#E4E1DC", "#B84900", "#FFF7F5F2", "#FFFFFFFF", "#FFFFFF"]
            : ["#242424", "#55514C", "#FFAD6C", "#FF292724", "#FF3A3733", "#FFFFFF"];
        for (int i=0;i<extraKeys.Length;i++) app.Resources[extraKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(extraColors[i]));
        app.Resources["SelectedPetal"] = new SolidColorBrush(Color.FromRgb(193,77,0));
        string[] materialKeys = ["MaterialChromeTint", "MaterialShellTint", "MaterialSpecularTop", "MaterialSpecularBottom", "MaterialInnerEdge", "MaterialOutline"];
        string[] materialColors = light ? ["#24FFFFFF", "#18FFFFFF", "#B3FFFFFF", "#12000000", "#38FFFFFF", "#26000000"] : ["#30201F1D", "#28201F1D", "#59FFFFFF", "#66000000", "#24FFFFFF", "#66FFFFFF"];
        for (int i = 0; i < materialKeys.Length; i++) app.Resources[materialKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(materialColors[i]));
        app.Resources["MaterialDepth"] = new LinearGradientBrush(new GradientStopCollection {
            new(((SolidColorBrush)app.Resources["MaterialSpecularTop"]).Color, 0), new(Colors.Transparent, .45),
            new(((SolidColorBrush)app.Resources["MaterialSpecularBottom"]).Color, 1) }, new Point(0, 0), new Point(1, 1));
        if (highContrast)
        {
            foreach (string key in new[] { "WindowBackground", "PanelBackground", "ControlBackground" }) app.Resources[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "PrimaryText", "SecondaryText", "ControlText", "ErrorText" }) app.Resources[key] = SystemColors.WindowTextBrush;
            foreach (string key in new[] { "WheelShell", "PetalBackground", "InsetBackground" }) app.Resources[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "BorderBrush", "ActionInk" }) app.Resources[key] = SystemColors.WindowTextBrush;
            app.Resources["SelectedPetal"] = SystemColors.HighlightBrush;
            app.Resources["SelectedPetalText"] = SystemColors.HighlightTextBrush;
            app.Resources["Accent"] = SystemColors.HighlightBrush;
            app.Resources["AccentText"] = SystemColors.HighlightTextBrush;
        }
        app.Resources["MaterialContentSurface"] = app.Resources["PanelBackground"];
        if (highContrast)
        {
            foreach (string key in materialKeys) app.Resources[key] = key is "MaterialChromeTint" or "MaterialShellTint" ? SystemColors.WindowBrush : Brushes.Transparent;
            app.Resources["MaterialDepth"] = Brushes.Transparent;
        }
        app.Resources["MotionEnabled"] = !reduceMotion && SystemParameters.ClientAreaAnimation && !highContrast;
        Changed?.Invoke();
    }
    internal void ApplyTypography(double scale)
    {
        scale = double.IsFinite(scale) ? Math.Clamp(scale, 1, 2.25) : 1;
        app.Resources["TextScaleFactor"] = scale;
        (string Key, double Value)[] sizes = [("TitleSize", 28), ("EditorTitleSize", 22), ("SectionSize", 16), ("BodySize", 14), ("MetadataSize", 12),
            ("TitleLeading", 34), ("EditorLeading", 28), ("SectionLeading", 22), ("BodyLeading", 20), ("MetadataLeading", 16)];
        foreach (var size in sizes) app.Resources[size.Key] = size.Value * scale;
    }
    public void Dispose()
    {
        disposed = true; Changed = null;
        SystemParameters.StaticPropertyChanged -= ParametersChanged;
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
    }
}
