using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Windows.Presentation;
using Application = System.Windows.Application;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class DesktopFixtureTests(DesktopFixture desktop)
{
    [Fact]
    public Task Startup_is_suppressed_before_actions_without_losing_product_resources() => desktop.RunAsync(async () =>
    {
        // Check before yielding: readiness must wait for WPF's actual startup callback.
        Assert.Equal(1, desktop.SuppressedStartupCallbacks);
        var app = Assert.IsAssignableFrom<App>(Application.Current);
        Assert.Equal(new Uri("pack://application:,,,/Citrus.Windows;component/CitrusResources.xaml"), app.Resources.Source);
        Assert.Equal(ShutdownMode.OnExplicitShutdown, app.ShutdownMode);
        Assert.False(app.HomeVisible);
        Assert.IsType<Style>(app.FindResource("CitrusButton"));

        using var theme = new ThemeManager(app);
        var settings = new SettingsWindow(new(), _ => Task.CompletedTask);
        try
        {
            theme.ApplyTypography(2);
            Assert.Null(PressFeedback.GetAnimator(settings));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, desktop.SuppressedStartupCallbacks);
            Assert.Equal(28d, app.FindResource("BodySize"));
            Assert.Null(PressFeedback.GetAnimator(settings));
            Assert.False(app.HomeVisible);
        }
        finally
        {
            settings.Close();
            theme.ApplyTypography(1);
        }
    });

    [Fact]
    public async Task Closing_a_window_keeps_the_dispatcher_available_to_render_another()
    {
        await desktop.RunAsync(async () =>
        {
            var first = new SettingsWindow(new(), _ => Task.CompletedTask);
            try
            {
                first.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.IsType<HwndSource>(PresentationSource.FromVisual(first));
            }
            finally { first.Close(); }
        });

        // A separate dispatch must still run after the previous window's close was processed.
        await desktop.RunAsync(async () =>
        {
            var app = Assert.IsAssignableFrom<App>(Application.Current);
            Assert.False(app.Dispatcher.HasShutdownStarted);
            Assert.Equal(1, desktop.SuppressedStartupCallbacks);
            using var theme = new ThemeManager(app);
            var second = new SettingsWindow(new(), _ => Task.CompletedTask);
            try
            {
                theme.ApplyTypography(2);
                second.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                second.UpdateLayout();
                Assert.IsType<HwndSource>(PresentationSource.FromVisual(second));
                Assert.Equal(28d, app.FindResource("BodySize"));
                Assert.Equal(28d, second.FontSize);
                Assert.Null(PressFeedback.GetAnimator(second));
                Assert.False(app.HomeVisible);

                var content = Assert.IsAssignableFrom<FrameworkElement>(second.Content);
                Assert.True(content.ActualWidth > 0 && content.ActualHeight > 0);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
                    (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                Assert.True(Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] != 0),
                    "The reopened window must render visible production content.");
            }
            finally
            {
                second.Close();
                theme.ApplyTypography(1);
            }
        });
    }
}
