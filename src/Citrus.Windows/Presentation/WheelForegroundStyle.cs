using System.Windows.Media;

namespace Citrus.Windows.Presentation;

/// <summary>Stable, readable retained controls above the single sampled glass backing.</summary>
internal static class WheelForegroundStyle
{
    internal static LinearGradientBrush Petal { get; } = MakePetal();
    internal static SolidColorBrush Selection { get; } = Make(255, 255, 85, 0);
    internal static SolidColorBrush Hub { get; } = Make(240, 255, 255, 255);
    internal static SolidColorBrush Capsule { get; } = Make(250, 255, 255, 255);
    private static SolidColorBrush Shell { get; } = Make(255, 235, 239, 239);
    private static SolidColorBrush SecondaryInk { get; } = Make(255, 80, 90, 94);

    internal static void Apply(ResourceDictionary resources, bool highContrast)
    {
        resources["PetalBackground"] = highContrast ? SystemColors.WindowBrush : Petal;
        resources["SelectedPetal"] = highContrast ? SystemColors.HighlightBrush : Selection;
        resources["WheelText"] = highContrast ? SystemColors.WindowTextBrush : Brushes.Black;
        resources["SelectedPetalText"] = highContrast ? SystemColors.HighlightTextBrush : Brushes.White;
        resources["PrimaryText"] = highContrast ? SystemColors.WindowTextBrush : Brushes.Black;
        resources["SecondaryText"] = highContrast ? SystemColors.WindowTextBrush : SecondaryInk;
        resources["WheelHubBackground"] = highContrast ? SystemColors.WindowBrush : Hub;
        resources["CapsuleBackground"] = highContrast ? SystemColors.WindowBrush : Capsule;
        resources["InsetBackground"] = highContrast ? SystemColors.WindowBrush : Hub;
        resources["WheelShell"] = highContrast ? SystemColors.WindowBrush : Shell;
        resources["BorderBrush"] = highContrast ? SystemColors.WindowTextBrush : Brushes.Transparent;
        resources["PetalRim"] = highContrast ? SystemColors.WindowTextBrush : Brushes.Transparent;
        resources["WheelHubRim"] = highContrast ? SystemColors.WindowTextBrush : Brushes.Transparent;
        foreach (string key in new[] { "MaterialOutline", "MaterialDepth", "MaterialInnerEdge" })
            resources[key] = Brushes.Transparent;
    }

    private static LinearGradientBrush MakePetal()
    {
        var brush = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(230, 255, 255, 255), 0),
            new(Color.FromArgb(217, 241, 244, 244), 1)
        }, new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Make(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}
