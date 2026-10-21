using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Citrus.Core;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using SystemColors = System.Windows.SystemColors;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class OpaqueMenuTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData("launcher")]
    [InlineData("workspace")]
    [InlineData("settings")]
    [InlineData("demo")]
    public Task Menu_chrome_is_white_and_its_labels_are_dark_even_with_the_dark_wheel_theme(string kind) => desktop.RunAsync(() =>
    {
        var app = Application.Current;
        var saved = app.Resources.Keys.Cast<object>().ToDictionary(key => key, key => app.Resources[key]);
        using var themes = new ThemeManager(app);
        Window? window = null;
        try
        {
            themes.ApplyPalette(ThemeChoice.Dark, false, false);
            using var services = new WindowPresentationService(app, themes, () => new CitrusSettings());
            window = kind switch
            {
                "settings" => new SettingsWindow(new(), _ => Task.CompletedTask),
                "demo" => new WheelDemoWindow(),
                _ => new MainWindow((App)app, startInLauncher: kind == "launcher")
            };
            var surface = (MaterialSurface)window.Content;
            services.Attach(window, surface);
            Assert.Equal(BackdropState.Solid, surface.State);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(surface.EffectiveTint).Color);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(window.FindResource("MaterialContentSurface")).Color);

            surface.Measure(new Size(440, 580));
            surface.Arrange(new Rect(0, 0, 440, 580));
            surface.UpdateLayout();
            var image = new RenderTargetBitmap(440, 580, 96, 96, PixelFormats.Pbgra32);
            image.Render(surface);
            Assert.Equal(Colors.White, Pixel(image, 12, 290));
            Assert.Equal(0, Pixel(image, 0, 0).A); // White remains inside the rounded native contour.
            string heading = kind switch { "settings" => "Settings", "demo" => "Try the wheel", _ => "Citrus" };
            var title = Descendants(surface).OfType<TextBlock>().First(value => value.Text == heading);
            var ink = Assert.IsType<SolidColorBrush>(title.Foreground).Color;
            Assert.Equal(255, ink.A);
            Assert.True(ink.R < 96 && ink.G < 96 && ink.B < 96, $"Expected dark title ink, found {ink}.");
            Assert.True(themes.IsDark);
            Assert.Equal(Color.FromRgb(0x21, 0x1f, 0x1d), Assert.IsType<SolidColorBrush>(app.FindResource("WindowBackground")).Color);
            if (window is WheelDemoWindow)
            {
                var innerWheel = Descendants(surface).OfType<Canvas>().Single(value => value.Name == "DemoWheelCanvas");
                Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(innerWheel.FindResource("PrimaryText")).Color);
                Assert.NotSame(app.FindResource("PrimaryText"), innerWheel.FindResource("PrimaryText"));
            }
        }
        finally
        {
            if (window is IDisposable disposable) disposable.Dispose();
            window?.Close();
            foreach (object key in app.Resources.Keys.Cast<object>().ToArray()) app.Resources.Remove(key);
            foreach (var pair in saved) app.Resources[pair.Key] = pair.Value;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Accessibility_palette_refresh_replaces_white_chrome_with_system_surfaces_and_restores_white() => desktop.RunAsync(() =>
    {
        var app = Application.Current;
        var saved = app.Resources.Keys.Cast<object>().ToDictionary(key => key, key => app.Resources[key]);
        using var themes = new ThemeManager(app);
        var window = new SettingsWindow(new(), _ => Task.CompletedTask);
        try
        {
            themes.ApplyPalette(ThemeChoice.Dark, false, false);
            using var services = new WindowPresentationService(app, themes, () => new CitrusSettings());
            var surface = (MaterialSurface)window.Content;
            services.Attach(window, surface);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(surface.EffectiveTint).Color);
            themes.ApplyPalette(ThemeChoice.Dark, false, true);
            services.Refresh();
            Assert.Same(SystemColors.WindowBrush, surface.EffectiveTint);
            Assert.Same(SystemColors.WindowTextBrush, window.FindResource("PrimaryText"));
            Assert.Same(SystemColors.HighlightBrush, window.FindResource("Accent"));
            Assert.Same(SystemColors.HighlightTextBrush, window.FindResource("AccentText"));
            themes.ApplyPalette(ThemeChoice.Dark, false, false);
            services.Refresh();
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(surface.EffectiveTint).Color);
            Assert.Equal(BackdropState.Solid, surface.State);
            Assert.True(themes.IsDark);
        }
        finally
        {
            window.Close();
            foreach (object key in app.Resources.Keys.Cast<object>().ToArray()) app.Resources.Remove(key);
            foreach (var pair in saved) app.Resources[pair.Key] = pair.Value;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Menu_attachments_create_no_optical_controller_and_leave_native_wheel_policy_enabled() => desktop.RunAsync(() =>
    {
        var app = Application.Current;
        var saved = app.Resources.Keys.Cast<object>().ToDictionary(key => key, key => app.Resources[key]);
        var preferences = new CitrusSettings { ReduceTransparency = false };
        using var themes = new ThemeManager(app);
        using var home = new MainWindow((App)app);
        var settings = new SettingsWindow(preferences, _ => Task.CompletedTask);
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            themes.ApplyPalette(ThemeChoice.Dark, false, false);
            using var services = new WindowPresentationService(app, themes, () => preferences);
            var monitor = (MaterialPreferenceMonitor)typeof(WindowPresentationService).GetField("policy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(services)!;
            var eligible = new MaterialDecision(true, false, "Owned eligible wheel policy");
            typeof(MaterialPreferenceMonitor).GetProperty(nameof(MaterialPreferenceMonitor.Decision))!.SetValue(monitor, eligible);
            services.Attach(home, (MaterialSurface)home.FindName("Chrome"));
            services.Attach(settings, (MaterialSurface)settings.FindName("Chrome"));
            services.Attach(wheel, (MaterialSurface)wheel.FindName("Shell"));
            var attachments = (IDictionary)typeof(WindowPresentationService).GetField("windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(services)!;
            foreach (Window menu in new Window[] { home, settings })
            {
                var attachment = attachments[menu]!;
                Assert.DoesNotContain(attachment.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
                    field => field.GetValue(attachment) is ChromeGlassController or LiveWheelGlass);
                Assert.Same(services.Animator, PressFeedback.GetAnimator(menu));
            }
            Assert.Equal(eligible, typeof(WheelWindow).GetField("materialDecision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel));
            Assert.False(preferences.ReduceTransparency);
            Assert.True(themes.IsDark);
        }
        finally
        {
            home.Dispose(); home.Close(); settings.Close(); wheel.Close();
            foreach (object key in app.Resources.Keys.Cast<object>().ToArray()) app.Resources.Remove(key);
            foreach (var pair in saved) app.Resources[pair.Key] = pair.Value;
        }
        return Task.CompletedTask;
    });

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
