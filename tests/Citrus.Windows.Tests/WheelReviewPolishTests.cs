using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelReviewPolishTests(DesktopFixture desktop)
{
    [Fact]
    public Task Compact_wheel_preserves_canonical_coordinates() => desktop.RunAsync(() =>
    {
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            Assert.Equal(368 * .78, wheel.Width);
            Assert.Equal(454 * .78, wheel.Height);
            var surface = Assert.IsType<Canvas>(wheel.FindName("Surface"));
            Assert.Equal(368, surface.Width);
            Assert.Equal(454, surface.Height);
            Assert.IsType<Viewbox>(wheel.Content);
        }
        finally { wheel.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Scaled_screen_points_round_trip_to_the_same_petals() => desktop.RunAsync(async () =>
    {
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            wheel.ShowAtPointer(OperationMode.Convert);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            wheel.UpdateLayout();
            var surface = Assert.IsType<Canvas>(wheel.FindName("Surface"));
            var center = surface.PointToScreen(new Point(184, 184));
            double pixelsPerUnit = (surface.PointToScreen(new Point(368, 0)) - surface.PointToScreen(new Point())).Length / 368;
            for (int index = 0; index < 7; index++)
            {
                var target = WheelGeometry.At(122, index * 360d / 7);
                var screen = surface.PointToScreen(new Point(target.X, target.Y));
                var local = surface.PointFromScreen(screen);
                // Native screen coordinates quantize to physical pixels.
                Assert.InRange(Math.Abs(target.X - local.X), 0, 1 / pixelsPerUnit + .001);
                Assert.InRange(Math.Abs(target.Y - local.Y), 0, 1 / pixelsPerUnit + .001);
                Assert.Equal(index, WheelGeometry.HitTest(local.X, local.Y, 7));
                Assert.True((screen - center).Length < 122 * System.Windows.Media.VisualTreeHelper.GetDpi(wheel).DpiScaleX * .9);
            }
        }
        finally { wheel.Close(); }
    });

    [Fact]
    public void Wheel_preserves_the_menu_refraction_and_frost_preset()
    {
        var material = GlassScene.Wheel(7, 1).Material;
        Assert.Equal(GlassMaterial.Citrus.FresnelIntensity * .7, material.FresnelIntensity);
        Assert.Equal(GlassMaterial.Citrus.GlareIntensity * .7, material.GlareIntensity);
        Assert.Equal(GlassMaterial.Citrus.ThicknessDip, material.ThicknessDip);
        Assert.Equal(GlassMaterial.Citrus.RefractionIndex, material.RefractionIndex);
        Assert.Equal(GlassMaterial.Citrus.TintAlpha, material.TintAlpha);
        Assert.Equal(.5958, GlassMaterial.Citrus.FresnelIntensity);
    }
}
