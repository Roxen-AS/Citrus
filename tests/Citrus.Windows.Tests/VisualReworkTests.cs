using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Windows.Presentation;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class VisualReworkTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(ThemeChoice.Light)]
    [InlineData(ThemeChoice.Dark)]
    public Task Bright_selection_and_primary_action_keep_readable_ink(ThemeChoice choice) => desktop.RunAsync(() =>
    {
        using var theme = new ThemeManager(Application.Current);
        try
        {
            theme.ApplyPalette(choice, true, false);
            foreach (var pair in new[] { ("Accent", "AccentText"), ("SelectedPetal", "SelectedPetalText"),
                ("PanelBackground", "PrimaryText"), ("PanelBackground", "SecondaryText") })
            {
                var surface = ((SolidColorBrush)Application.Current.FindResource(pair.Item1)).Color;
                var ink = ((SolidColorBrush)Application.Current.FindResource(pair.Item2)).Color;
                Assert.True(Contrast(surface, ink) >= 4.5, $"{choice}: {pair} lacks readable contrast.");
            }
            var petal = new WheelPetal(new ImageEngine().Operations.Single(operation => operation.Id == "image.convert.png"), 0, 9);
            petal.ApplyTextScale(2);
            var label = petal.Children.OfType<TextBlock>().Single();
            Assert.Equal("PNG", label.Text);
            Assert.Equal(Visibility.Visible, label.Visibility);
            Assert.True(label.FontSize > 20);
            petal.SetSelected(true, false);
            Assert.Same(Application.Current.FindResource("SelectedPetalText"), label.Foreground);
        }
        finally { theme.Apply(ThemeChoice.Light, true); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(ThemeChoice.Light, 1d)]
    [InlineData(ThemeChoice.Dark, 1d)]
    [InlineData(ThemeChoice.Light, 2d)]
    public Task Empty_workspace_has_a_reachable_drop_action_and_no_empty_history(ThemeChoice choice, double textScale) => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        using var window = new MainWindow((App)Application.Current) { Width = 860, Height = 640, ShowActivated = false };
        try
        {
            theme.Apply(choice, true);
            theme.ApplyTypography(textScale);
            window.Configure(new Citrus.Core.OperationCatalog([new ImageEngine()]), []);
            window.ApplyTextScale(textScale);
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var empty = (Border)window.FindName("EmptyState");
            var add = ((StackPanel)empty.Child).Children.OfType<System.Windows.Controls.Button>().Single(button => Equals(button.Content, "Add files"));
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("JobsPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("Inspector")).Visibility);
            add.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VisualArtifacts.AssertInside(add, (FrameworkElement)window.Content);
            VisualArtifacts.Capture((FrameworkElement)window.Content, $"workspace-empty-{choice}-text{textScale * 100:0}", 1, choice,
                new { textScale, captureScope = "WPF-only, original application rendering" });
        }
        finally { window.Dispose(); window.Close(); theme.ApplyTypography(1); theme.Apply(ThemeChoice.Light, true); }
    });

    [Fact]
    public void Fresh_settings_use_light_without_replacing_explicit_saved_choices()
    {
        using var files = new NativeFiles();
        var store = new SettingsStore(files.Root);
        Assert.Equal(ThemeChoice.Light, store.Load().Theme);
        foreach (var choice in new[] { ThemeChoice.System, ThemeChoice.Dark })
        {
            store.Save(new CitrusSettings { Theme = choice });
            Assert.Equal(choice, store.Load().Theme);
        }
    }

    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
