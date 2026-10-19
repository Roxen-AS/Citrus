using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using Citrus.Core;

namespace Citrus.Windows;

/// <summary>Small image-only option flow; constructed only after OLE has returned.</summary>
public sealed class ToolOptionsWindow : Window
{
    private readonly StackPanel content = new() { Margin = new Thickness(24) };
    private readonly TextBlock error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    public OperationOptions Options { get; private set; } = new();

    public ToolOptionsWindow(OperationDescriptor operation)
    {
        Title = operation.Label + " · Citrus";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(32, 36, 40));
        Foreground = Brushes.WhiteSmoke;
        Content = content;
        content.Children.Add(new TextBlock { Text = operation.Label, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        TextBox? width = null, height = null, quality = null;
        ComboBox? rotation = null;
        CheckBox? aspect = null;
        if (operation.Id == "image.resize")
        {
            width = Field("Width in pixels (optional)", "");
            height = Field("Height in pixels (optional)", "");
            aspect = new CheckBox { Content = "Preserve aspect ratio", IsChecked = true, Foreground = Foreground, Margin = new Thickness(0, 8, 0, 0) };
            content.Children.Add(aspect);
        }
        else if (operation.Id == "image.compress") quality = Field("Quality · 1–100", "85");
        else if (operation.Id == "image.rotate")
        {
            content.Children.Add(new TextBlock { Text = "Clockwise rotation", Margin = new Thickness(0, 0, 0, 8) });
            rotation = new ComboBox { ItemsSource = new[] { 90, 180, 270 }, SelectedIndex = 0, Padding = new Thickness(8) };
            content.Children.Add(rotation);
        }
        content.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Background = Brushes.DimGray, Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0) };
        var create = new Button { Content = "Create copy", IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        content.Children.Add(buttons);
        create.Click += (_, _) =>
        {
            int? Parse(TextBox? box) => string.IsNullOrWhiteSpace(box?.Text) ? null : int.Parse(box.Text, CultureInfo.InvariantCulture);
            try
            {
                var parsedWidth = Parse(width);
                var parsedHeight = Parse(height);
                int parsedQuality = Parse(quality) ?? 85;
                if (operation.Id == "image.resize" && (parsedWidth is <= 0 || parsedHeight is <= 0 || (parsedWidth is null && parsedHeight is null)))
                    throw new FormatException("Enter at least one positive pixel dimension.");
                if (parsedQuality is < 1 or > 100) throw new FormatException("Quality must be between 1 and 100.");
                Options = new() { Width = parsedWidth, Height = parsedHeight, PreserveAspectRatio = aspect?.IsChecked ?? true, Quality = parsedQuality, RotationDegrees = (int?)rotation?.SelectedItem ?? 90 };
                DialogResult = true;
            }
            catch (Exception failure) when (failure is FormatException or OverflowException) { error.Text = failure is OverflowException ? "The number is too large." : failure.Message; }
        };
    }

    private TextBox Field(string label, string value)
    {
        content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 6) });
        var box = new TextBox { Text = value, Padding = new Thickness(8), FontSize = 14 };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        content.Children.Add(box);
        return box;
    }
}
