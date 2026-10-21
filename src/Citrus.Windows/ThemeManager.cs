using System.ComponentModel;
using System.Windows.Media;
using Microsoft.Win32;

namespace Citrus.Windows;

internal sealed class ThemeManager : IDisposable
{
    private readonly Application app;
    private ThemeChoice choice;
    private bool reduceMotion, disposed, highContrast = SystemParameters.HighContrast;
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
        this.highContrast = highContrast;
        bool light = selected == ThemeChoice.Light;
        if (selected == ThemeChoice.System)
        {
            using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (settings?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        IsDark = !light;
        var colors = light
            ? new[] { "#FAF8F6", "#FFFFFF", "#29231F", "#5C544D", "#A42615", "#FF620F", "#29231F", "#FFFFFF", "#29231F" }
            : new[] { "#211F1D", "#2D2926", "#FAF8F6", "#C8BFB7", "#FFB49C", "#FF620F", "#211F1D", "#37312C", "#FAF8F6" };
        string[] keys = ["WindowBackground", "PanelBackground", "PrimaryText", "SecondaryText", "ErrorText", "Accent", "AccentText", "ControlBackground", "ControlText"];
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        string[] extraKeys = ["InsetBackground", "BorderBrush", "ActionInk", "WheelShell", "PetalBackground", "SelectedPetalText"];
        string[] extraColors = light ? ["#EEEAE5", "#DAD3CB", "#29231F", "#FFEBEFEF", "#FFFFFFFF", "#29231F"]
            : ["#282421", "#50473E", "#FF620F", "#FF211F1D", "#FF37312C", "#211F1D"];
        for (int i=0;i<extraKeys.Length;i++) app.Resources[extraKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(extraColors[i]));
        // Opaque, static lighting gives the fallback depth without simulating a desktop backdrop.
        app.Resources["PetalBackground"] = SurfaceGradient(light ? ["#F5F7F7", "#EFF2F2", "#E7EBEB"] : ["#50453B", "#40372F", "#322C26"]);
        app.Resources["PetalRim"] = SurfaceGradient(light ? ["#D3DADB", "#C8CFD0"] : ["#8D7B68", "#211A15"]);
        app.Resources["WheelHubBackground"] = SurfaceGradient(light ? ["#FBFCFC", "#FBFCFC", "#F8FAFA"] : ["#2A221B", "#342C24", "#40362D"]);
        app.Resources["WheelHubRim"] = SurfaceGradient(light ? ["#F4F6F6", "#FFFFFF"] : ["#211A15", "#786653"]);
        app.Resources["SelectedPetal"] = new SolidColorBrush(Color.FromRgb(255,85,0));
        string[] materialKeys = ["MaterialChromeTint", "MaterialShellTint", "MaterialSpecularTop", "MaterialSpecularBottom", "MaterialInnerEdge", "MaterialOutline"];
        string[] materialColors = light ? ["#24FFFFFF", "#18FFFFFF", "#B3FFFFFF", "#12000000", "#38FFFFFF", "#26000000"] : ["#3028231E", "#2828231E", "#59FFFFFF", "#66000000", "#24FFFFFF", "#66FFFFFF"];
        for (int i = 0; i < materialKeys.Length; i++) app.Resources[materialKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(materialColors[i]));
        app.Resources["MaterialDepth"] = new LinearGradientBrush(new GradientStopCollection {
            new(((SolidColorBrush)app.Resources["MaterialSpecularTop"]).Color, 0), new(Colors.Transparent, .45),
            new(((SolidColorBrush)app.Resources["MaterialSpecularBottom"]).Color, 1) }, new Point(0, 0), new Point(1, 1));
        app.Resources["WheelText"] = light ? Brushes.Black : Brushes.White;
        app.Resources["SelectedPetalText"] = Brushes.White;
        if (highContrast)
        {
            app.Resources["WheelText"] = SystemColors.WindowTextBrush;
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
        app.Resources["CapsuleBackground"] = highContrast ? SystemColors.WindowBrush : new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FFFFFF" : "#32383B"));
        app.Resources["WheelSecondaryText"] = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#748188" : "#C2CCD1"));
        app.Resources["SliderThumb"] = highContrast ? SystemColors.HighlightBrush : Brushes.White;
        app.Resources["ReferenceActionBackground"] = highContrast ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromRgb(211, 69, 0));
        app.Resources["ReferenceActionText"] = highContrast ? SystemColors.HighlightTextBrush : Brushes.White;
        if (highContrast)
        {
            foreach (string key in materialKeys) app.Resources[key] = key is "MaterialChromeTint" or "MaterialShellTint" ? SystemColors.WindowBrush : Brushes.Transparent;
            app.Resources["MaterialDepth"] = Brushes.Transparent;
        }
        app.Resources["MotionEnabled"] = !reduceMotion && SystemParameters.ClientAreaAnimation && !highContrast;
        Changed?.Invoke();
    }
    internal void ApplyOpaqueChromePalette(ResourceDictionary resources)
    {
        // Window-local chrome stays white while the wheel retains the selected application theme.
        foreach (string key in new[] { "WindowBackground", "PanelBackground", "ControlBackground", "MaterialContentSurface", "MaterialChromeTint" })
            resources[key] = highContrast ? SystemColors.WindowBrush : Brushes.White;
        (string Key, string Color)[] colors = [
            ("PrimaryText", "#29231F"), ("SecondaryText", "#5C544D"), ("ControlText", "#29231F"),
            ("ErrorText", "#A42615"), ("ActionInk", "#29231F"), ("AccentText", "#29231F"),
            ("BorderBrush", "#DAD3CB"), ("InsetBackground", "#EEEAE5"), ("Accent", "#FF620F"),
            ("ReferenceActionBackground", "#D34500"), ("ReferenceActionText", "#FFFFFF")];
        foreach (var (key, color) in colors)
        {
            Brush brush;
            if (highContrast)
                brush = key switch
                {
                    "InsetBackground" => SystemColors.WindowBrush,
                    "Accent" or "ReferenceActionBackground" => SystemColors.HighlightBrush,
                    "AccentText" or "ReferenceActionText" => SystemColors.HighlightTextBrush,
                    _ => SystemColors.WindowTextBrush
                };
            else
            {
                brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                brush.Freeze();
            }
            resources[key] = brush;
        }
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
