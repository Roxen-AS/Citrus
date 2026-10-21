using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Citrus.Core;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class StudioMaterialIntegrationTests(DesktopFixture desktop)
{
    [Fact]
    public Task Selection_keeps_its_solid_orange_foreground_when_backing_arrives() => desktop.RunAsync(() =>
    {
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            typeof(WheelWindow).GetMethod("SetMaterialState", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(wheel, [BackdropState.LiveRefractiveGlass]);
            var fill = Assert.IsType<SolidColorBrush>(wheel.FindResource("SelectedPetal"));
            Assert.Equal(System.Windows.Media.Color.FromRgb(255, 85, 0), fill.Color);
            typeof(WheelWindow).GetMethod("SetMaterialState", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(wheel, [BackdropState.Solid]);
            Assert.Equal(255, Assert.IsType<SolidColorBrush>(wheel.FindResource("SelectedPetal")).Color.A);
        }
        finally { wheel.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Optical_images_are_below_the_retained_petal_glyphs() => desktop.RunAsync(() =>
    {
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            var canvas = (Canvas)wheel.FindName("Presentation");
            var glyphs = (Canvas)wheel.FindName("Sectors");
            foreach (string image in new[] { "GlassBody", "GlassLighting" })
                Assert.True(canvas.Children.IndexOf((UIElement)wheel.FindName(image)) < canvas.Children.IndexOf(glyphs));
        }
        finally { wheel.Close(); }
        return Task.CompletedTask;
    });
}
