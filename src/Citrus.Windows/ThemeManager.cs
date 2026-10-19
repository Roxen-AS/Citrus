using System.ComponentModel;
using System.Windows.Media;
using Microsoft.Win32;

namespace Citrus.Windows;

internal sealed class ThemeManager : IDisposable
{
    private readonly Application app;
    private ThemeChoice choice;
    public ThemeManager(Application app)
    {
        this.app = app;
        SystemParameters.StaticPropertyChanged += ParametersChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
    }
    private void ParametersChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Refresh();
    private void Refresh() => app.Dispatcher.BeginInvoke(new Action(() => Apply(choice)));
    public void Apply(ThemeChoice selected)
    {
        choice = selected;
        bool light = selected == ThemeChoice.Light;
        if (selected == ThemeChoice.System)
        {
            using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (settings?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        var colors = light
            ? new[] { "#F5F6F7", "#E3E7EB", "#202428", "#424B54", "#A42615", "#DC6A12", "#FFFFFF", "#FFFFFF", "#202428" }
            : new[] { "#202428", "#2B3137", "#F3EFE9", "#BAC3CB", "#FFB49C", "#F88C37", "#22170E", "#FFFFFF", "#202428" };
        string[] keys = ["WindowBackground", "PanelBackground", "PrimaryText", "SecondaryText", "ErrorText", "Accent", "AccentText", "ControlBackground", "ControlText"];
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        if (SystemParameters.HighContrast)
        {
            foreach (string key in new[] { "WindowBackground", "PanelBackground", "ControlBackground" }) app.Resources[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "PrimaryText", "SecondaryText", "ControlText", "ErrorText" }) app.Resources[key] = SystemColors.WindowTextBrush;
            app.Resources["Accent"] = SystemColors.HighlightBrush;
            app.Resources["AccentText"] = SystemColors.HighlightTextBrush;
        }
        app.Resources["MotionEnabled"] = SystemParameters.ClientAreaAnimation;
    }
    public void Dispose()
    {
        SystemParameters.StaticPropertyChanged -= ParametersChanged;
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
    }
}
