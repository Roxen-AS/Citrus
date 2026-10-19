using System.Windows;
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
}
