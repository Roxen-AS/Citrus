using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using FlowDirection = System.Windows.FlowDirection;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class MenuPolishTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public Task Close_control_is_at_the_right_edge_and_keeps_title_centered(int rightToLeft) => desktop.RunAsync(() =>
    {
        var title = new TextBlock { Text = "Crop Video", TextTrimming = TextTrimming.CharacterEllipsis };
        var header = new ChromeHeader { Content = title };
        var window = new Window { Content = header, Width = 420, Height = 110, ShowActivated = false,
            FlowDirection = rightToLeft == 0 ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
        bool closed = false; window.Closed += (_, _) => closed = true;
        try
        {
            window.Show(); window.UpdateLayout(); header.ApplyTemplate();
            var close = Assert.IsType<Button>(header.Template.FindName("PART_Close", header));
            var bounds = ScreenBounds(close); var headerBounds = ScreenBounds(header);
            double deviceScale = headerBounds.Width / header.ActualWidth;
            Assert.True(bounds.Left > headerBounds.Left + headerBounds.Width / 2, "Close must stay at the physical right edge, including RTL layouts.");
            Assert.InRange((headerBounds.Right - bounds.Right) / deviceScale, 8, 20);
            Assert.True(close.ActualWidth >= 32 && close.ActualHeight >= 32);
            var text = ScreenBounds(title);
            Assert.InRange(Math.Abs((text.Left + text.Right) / 2 - (headerBounds.Left + headerBounds.Right) / 2), 0, deviceScale);
            Assert.Equal("Close window", AutomationProperties.GetName(close));
            Assert.True(close.Focusable); Assert.NotNull(close.FocusVisualStyle);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(closed);
        }
        finally { if (!closed) window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(ThemeChoice.Light, 1d, false)]
    [InlineData(ThemeChoice.Dark, 1d, false)]
    [InlineData(ThemeChoice.Light, 2d, false)]
    [InlineData(ThemeChoice.Light, 1d, true)]
    public Task Main_menu_and_settings_render_with_reachable_actions(ThemeChoice choice, double scale, bool highContrast) => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        theme.ApplyPalette(choice, true, highContrast); theme.ApplyTypography(scale);
        using var home = new MainWindow((App)Application.Current, startInLauncher: true) { ShowActivated = false };
        var settings = new SettingsWindow(new(), _ => Task.CompletedTask) { ShowActivated = false };
        try
        {
            home.Show(); settings.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            home.UpdateLayout(); settings.UpdateLayout();
            string suffix = highContrast ? "-highcontrast" : "";
            Capture(home, $"menu-{choice}-text{scale * 100:0}{suffix}");
            Capture(settings, $"menu-settings-{choice}-text{scale * 100:0}{suffix}");
            var root = (FrameworkElement)home.FindName("LauncherRoot");
            Assert.True(root.ActualWidth > 0);
            var buttons = Descendants(root).OfType<Button>().ToArray();
            Assert.Contains(buttons, button => Equals(button.Content, "Add files…"));
            foreach (var button in buttons.Where(button => Equals(button.Content, "Settings") || Equals(button.Content, "Open workspace")))
            {
                Assert.True(button.IsVisible);
                Assert.True(root.RenderSize.Height + 1 >= button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize)).Bottom);
            }
        }
        finally { settings.Close(); home.Dispose(); home.Close(); theme.ApplyTypography(1); theme.Apply(ThemeChoice.Light, true); }
    });

    [Fact]
    public Task Launcher_icons_follow_the_remapped_modes() => desktop.RunAsync(() =>
    {
        using var home = new MainWindow((App)Application.Current, startInLauncher: true);
        try
        {
            home.UpdateGestureGuide(new() { ShiftMode = OperationMode.Actions, AltMode = OperationMode.Convert, ControlMode = OperationMode.Tools });
            foreach (var (name, icon) in new[] { ("LauncherShiftIcon", "Icon.File"), ("LauncherAltIcon", "Icon.Convert"), ("LauncherControlIcon", "Icon.Tools") })
                Assert.Same(home.FindResource(icon), ((System.Windows.Shapes.Path)home.FindName(name)).Data);
        }
        finally { home.Dispose(); home.Close(); }
        return Task.CompletedTask;
    });

    private static Rect ScreenBounds(FrameworkElement element) => new(element.PointToScreen(new(0, 0)), element.PointToScreen(new(element.ActualWidth, element.ActualHeight)));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
    private static void Capture(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "menu-polish");
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
}
