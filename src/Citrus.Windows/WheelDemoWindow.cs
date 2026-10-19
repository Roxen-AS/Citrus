using System.Windows.Controls;
using System.Windows.Input;
using Citrus.Core;

namespace Citrus.Windows;

/// <summary>Local demonstration only: no file paths, OLE handlers, audio or submission callback.</summary>
public sealed class WheelDemoWindow : Window
{
    public WheelDemoWindow()
    {
        Title = "Try the wheel · demonstration"; Width = 460; Height = 550;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackground"); SetResourceReference(ForegroundProperty, "PrimaryText");
        SetResourceReference(FontFamilyProperty, "AppFont"); SetResourceReference(FontSizeProperty, "BodySize");
        var panel = new StackPanel { Margin = new(24) };
        panel.SetResourceReference(Panel.BackgroundProperty, "MaterialContentSurface");
        var heading = new TextBlock { Text = "Try a little glide", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "EditorTitleSize");
        panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = "Demonstration · sample image.png\nNo files are opened or changed.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) });
        var canvas = new Canvas { Width = WheelGeometry.Width, Height = WheelGeometry.Width, Background = System.Windows.Media.Brushes.Transparent, Focusable = true };
        OperationDescriptor[] operations = [new("image.png", "PNG", OperationMode.Convert, ".png", false, false), new("image.resize", "Resize", OperationMode.Tools, null, false, true), new("image.rotate", "Rotate", OperationMode.Tools, null, false, true), new("action.reveal", "Reveal", OperationMode.Actions, null, false, false)];
        var petals = operations.Select((operation, index) => new WheelPetal(operation, index, operations.Length)).ToArray();
        foreach (var petal in petals) canvas.Children.Add(petal);
        var center = new TextBlock { Text = "DEMO\nGlide to a choice", Width = 140, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Canvas.SetLeft(center, WheelGeometry.CenterX - 70); Canvas.SetTop(center, WheelGeometry.CenterY - 20); canvas.Children.Add(center);
        int active = -1;
        void Select(int selected) { active = selected; for (int i = 0; i < petals.Length; i++) petals[i].SetSelected(i == active, false); center.Text = selected < 0 ? "DEMO\nCenter cancels" : operations[selected].Label + "\nSample only"; }
        canvas.MouseMove += (_, e) => { var point = e.GetPosition(canvas); Select(WheelGeometry.HitTest(point.X, point.Y, petals.Length)); };
        canvas.MouseLeave += (_, _) => Select(-1);
        canvas.KeyDown += (_, e) => { if (e.Key is Key.Right or Key.Down) { Select((active + 1) % petals.Length); e.Handled = true; } else if (e.Key is Key.Left or Key.Up) { Select((active + petals.Length - 1) % petals.Length); e.Handled = true; } };
        System.Windows.Automation.AutomationProperties.SetName(canvas, "Safe wheel demonstration. Use arrow keys to explore sample choices.");
        panel.Children.Add(canvas);
        var close = new Button { Content = "Got it", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close(); panel.Children.Add(close);
        Content = new Citrus.Windows.Presentation.MaterialSurface { Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; Loaded += (_, _) => canvas.Focus();
    }
}
