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
            ? new[] { "#F5F7FA", "#FFFFFF", "#17212B", "#596574", "#A42615", "#FF6B16", "#17212B", "#FFFFFF", "#202428" }
            : new[] { "#171B22", "#222832", "#F5F7FA", "#B8C3D1", "#FFB49C", "#FF6B16", "#171B22", "#272F3A", "#F5F7FA" };
        string[] keys = ["WindowBackground", "PanelBackground", "PrimaryText", "SecondaryText", "ErrorText", "Accent", "AccentText", "ControlBackground", "ControlText"];
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        string[] extraKeys = ["InsetBackground", "BorderBrush", "ActionInk", "WheelShell", "PetalBackground", "SelectedPetalText"];
        string[] extraColors = light ? ["#EDF1F6", "#DCE2EA", "#17212B", "#FFF5F7FA", "#FFFFFFFF", "#17212B"]
            : ["#1C222B", "#3C4654", "#FF6B16", "#FF171B22", "#FF2B3441", "#171B22"];
        for (int i=0;i<extraKeys.Length;i++) app.Resources[extraKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(extraColors[i]));
        // Opaque, static lighting gives the fallback depth without simulating a desktop backdrop.
        app.Resources["PetalBackground"] = SurfaceGradient(light ? ["#FFFFFF", "#F5F8FC", "#DCE5EF"] : ["#3B4655", "#2B3441", "#222A35"]);
        app.Resources["PetalRim"] = SurfaceGradient(light ? ["#FFFFFF", "#B7C6D7"] : ["#637187", "#141B24"]);
        app.Resources["WheelHubBackground"] = SurfaceGradient(light ? ["#DEE7F0", "#EAF0F6", "#F4F7FA"] : ["#161D27", "#1D2632", "#242F3C"]);
        app.Resources["WheelHubRim"] = SurfaceGradient(light ? ["#B7C6D7", "#FFFFFF"] : ["#101720", "#48566B"]);
        app.Resources["SelectedPetal"] = new SolidColorBrush(Color.FromRgb(255,107,22));
        string[] materialKeys = ["MaterialChromeTint", "MaterialShellTint", "MaterialSpecularTop", "MaterialSpecularBottom", "MaterialInnerEdge", "MaterialOutline"];
        string[] materialColors = light ? ["#24FFFFFF", "#18FFFFFF", "#B3FFFFFF", "#12000000", "#38FFFFFF", "#26000000"] : ["#30202A38", "#28202A38", "#59FFFFFF", "#66000000", "#24FFFFFF", "#66FFFFFF"];
        for (int i = 0; i < materialKeys.Length; i++) app.Resources[materialKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(materialColors[i]));
        app.Resources["MaterialDepth"] = new LinearGradientBrush(new GradientStopCollection {
            new(((SolidColorBrush)app.Resources["MaterialSpecularTop"]).Color, 0), new(Colors.Transparent, .45),
            new(((SolidColorBrush)app.Resources["MaterialSpecularBottom"]).Color, 1) }, new Point(0, 0), new Point(1, 1));
        if (highContrast)
        {
            foreach (string key in new[] { "WindowBackground", "PanelBackground", "ControlBackground" }) app.Resources[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "PrimaryText", "SecondaryText", "ControlText", "ErrorText" }) app.Resources[key] = SystemColors.WindowTextBrush;
            foreach (string key in new[] { "WheelShell", "PetalBackground", "WheelHubBackground", "InsetBackground" }) app.Resources[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "BorderBrush", "ActionInk", "PetalRim", "WheelHubRim" }) app.Resources[key] = SystemColors.WindowTextBrush;
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
    private static LinearGradientBrush SurfaceGradient(string[] colors)
    {
        var stops = new GradientStopCollection();
        for (int i = 0; i < colors.Length; i++)
            stops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors[i]), colors.Length == 3 && i == 1 ? .46 : (double)i / (colors.Length - 1)));
        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
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
