using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Citrus.Windows.Presentation;
using Citrus.Core;
using SystemColors = System.Windows.SystemColors;
using Application = System.Windows.Application;
namespace Citrus.Windows.Tests;
[Collection("Desktop")]
public sealed class GlassSurfaceIntegrationTests(DesktopFixture desktop)
{
    [Fact]
    public Task Native_material_state_not_inferred_from_alpha_color() => desktop.RunAsync(() =>
    {
        var native = new MaterialSurface { State = BackdropState.ConfiguredNativeAcrylic };
        var fallback = new MaterialSurface();
        Assert.Equal(BackdropState.Solid, fallback.State);
        Assert.Equal(BackdropState.ConfiguredNativeAcrylic, native.State);
        Assert.Equal(255, ((SolidColorBrush)fallback.EffectiveTint).Color.A);
        Assert.True(((SolidColorBrush)native.EffectiveTint).Color.A < 255);
        return Task.CompletedTask;
    });
    [Fact]
    public Task Twice_text_scale_reflows_without_double_dpi_scaling() => desktop.RunAsync(() =>
    {
        using var theme = new ThemeManager(Application.Current);
        try
        {
            theme.ApplyTypography(2);
            Assert.Equal(28d, Application.Current.FindResource("BodySize"));
            Assert.Equal(40d, Application.Current.FindResource("BodyLeading"));
            theme.ApplyTypography(2);
            Assert.Equal(28d, Application.Current.FindResource("BodySize"));
            var settings = new SettingsWindow(new(), _ => Task.CompletedTask);
            Assert.Null(PressFeedback.GetAnimator(settings));
            settings.Close();
        }
        finally { theme.ApplyTypography(1); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task Highcontrast_has_opaque_system_surfaces_and_focus() => desktop.RunAsync(() =>
    {
        using var theme = new ThemeManager(Application.Current);
        try
        {
            theme.ApplyPalette(ThemeChoice.Dark, false, true);
            foreach (string key in new[] { "WindowBackground", "PanelBackground", "ControlBackground", "WheelShell", "PetalBackground" })
            {
                Assert.Same(SystemColors.WindowBrush, Application.Current.FindResource(key));
                Assert.Equal(255, ((SolidColorBrush)Application.Current.FindResource(key)).Color.A);
            }
            Assert.Same(SystemColors.HighlightBrush, Application.Current.FindResource("SelectedPetal"));
            Assert.Same(SystemColors.HighlightTextBrush, Application.Current.FindResource("SelectedPetalText"));
            Assert.False((bool)Application.Current.FindResource("MotionEnabled"));
            var button = new System.Windows.Controls.Button();
            button.Style = (Style)Application.Current.FindResource("CitrusButton");
            Assert.NotNull(button.FocusVisualStyle);
        }
        finally { theme.Apply(ThemeChoice.Light, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Content_cards_remain_solid_inside_native_chrome() => desktop.RunAsync(() =>
    {
        var window = new MainWindow((App)Application.Current);
        try
        {
            var chrome = (MaterialSurface)window.FindName("Chrome");
            chrome.State = BackdropState.ConfiguredNativeAcrylic;
            Assert.True(((SolidColorBrush)chrome.EffectiveTint).Color.A < 255);
            Assert.Equal(255, ((SolidColorBrush)((Border)window.FindName("Inspector")).Background).Color.A);
            Assert.Equal(255, ((SolidColorBrush)Application.Current.FindResource("MaterialContentSurface")).Color.A);
        }
        finally { window.Dispose(); window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Shared_services_attach_each_window_and_clear_animator_on_teardown() => desktop.RunAsync(() =>
    {
        using var theme = new ThemeManager(Application.Current);
        using var services = new WindowPresentationService(Application.Current, theme, () => new CitrusSettings { ReduceTransparency = true });
        var first = new SettingsWindow(new(), _ => Task.CompletedTask);
        var second = new ShelfWindow();
        try
        {
            services.Attach(first, (MaterialSurface)first.FindName("Chrome"));
            services.Attach(second, (MaterialSurface)second.FindName("Chrome"));
            Assert.Same(PressFeedback.GetAnimator(first), PressFeedback.GetAnimator(second));
            Assert.Same(services.Animator, PressFeedback.GetAnimator(first));
            first.Show(); second.Show();
            Assert.Equal(BackdropState.Solid, ((MaterialSurface)first.FindName("Chrome")).State);
            first.Close();
            Assert.Null(PressFeedback.GetAnimator(first));
            Assert.Same(services.Animator, PressFeedback.GetAnimator(second));
            services.Dispose();
            Assert.Null(PressFeedback.GetAnimator(second));
        }
        finally { first.Close(); second.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Twice_text_scale_keeps_narrow_workspace_inspector_reachable() => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        var window = new MainWindow((App)Application.Current) { Width = 860, Height = 640, ShowActivated = false };
        try
        {
            theme.ApplyTypography(2);
            window.ApplyTextScale(2);
            window.Show(); window.UpdateLayout();
            var run = (FrameworkElement)window.FindName("RunButton");
            run.BringIntoView();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var scroll = (ScrollViewer)window.FindName("WorkspaceScroll");
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.Equal(2, Grid.GetRow((FrameworkElement)window.FindName("Inspector")));
            var position = run.TransformToAncestor(scroll).Transform(new System.Windows.Point(0, 0));
            Assert.InRange(position.Y, 0, scroll.ActualHeight - run.ActualHeight + 1);
            Assert.True(run.ActualWidth <= scroll.ActualWidth);
        }
        finally { window.Dispose(); window.Close(); theme.ApplyTypography(1); }
    });

    [Fact]
    public Task Caption_and_hint_have_opaque_backing_within_existing_bounds() => desktop.RunAsync(() =>
    {
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            var plate = (Border)wheel.FindName("CaptionPlate");
            Assert.Equal(62, Canvas.GetLeft(plate)); Assert.Equal(364, Canvas.GetTop(plate));
            Assert.Equal(244, plate.Width); Assert.Equal(46, plate.Height);
            Assert.Equal(255, ((SolidColorBrush)plate.Background).Color.A);
            var next = (Border)wheel.FindName("Next");
            Assert.Equal(310, Canvas.GetLeft(next)); Assert.Equal(40, next.Width);
        }
        finally { wheel.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task Gif_short_petal_preserves_full_label_and_operation_identity() => desktop.RunAsync(() =>
    {
        var operation = new OperationDescriptor("image.convert.gif", "GIF (still image)", OperationMode.Convert, "gif", false, false);
        var petal = new WheelPetal(operation, 0, 9);
        Assert.Equal("GIF", petal.Children.OfType<TextBlock>().Single().Text);
        Assert.Equal(operation.Label, System.Windows.Automation.AutomationProperties.GetName(petal));
        Assert.Equal("image.convert.gif", operation.Id);
        return Task.CompletedTask;
    });
}
