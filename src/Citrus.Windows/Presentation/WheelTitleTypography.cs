using System.Windows.Controls;

namespace Citrus.Windows.Presentation;

/// <summary>Fixed reference roles; no destination name changes its own font size.</summary>
internal static class WheelTitleTypography
{
    internal static void ApplyPetal(TextBlock title, bool format, double scale)
    {
        title.FontSize = (format ? Math.Min(20.5 * scale, 22) : Math.Min(11.5 * scale, 13)) * WheelPresentationMetrics.LabelCompensation;
        title.FontWeight = format ? FontWeights.Bold : FontWeights.SemiBold;
        title.TextWrapping = TextWrapping.Wrap;
        title.TextTrimming = TextTrimming.None;
        title.LineHeight = title.FontSize * 1.18;
        title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
    }
    internal static void ApplyHub(TextBlock title, double scale)
    {
        title.FontSize = Math.Min(16 * scale, 18) * WheelPresentationMetrics.LabelCompensation;
        title.FontWeight = FontWeights.Normal;
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.None;
    }
    internal static void ApplyDescription(TextBlock title, double scale)
    {
        title.FontSize = Math.Min(14 * scale, 16) * WheelPresentationMetrics.LabelCompensation;
        title.FontWeight = FontWeights.Normal;
        title.TextWrapping = TextWrapping.Wrap;
        title.TextTrimming = TextTrimming.None;
    }
}
