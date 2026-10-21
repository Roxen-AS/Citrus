using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using Citrus.Core;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class SoftPendingWheelTests(DesktopFixture desktop)
{
    private static readonly string[] Edges = ["MaterialOutline", "MaterialDepth", "MaterialInnerEdge", "PetalRim"];

    [Fact]
    public Task Pending_selection_cannot_restart_the_white_specular_contour() => desktop.RunAsync(() =>
    {
        var wheel = new WheelWindow(new OperationCatalog([]));
        using var animator = new SpringAnimator(() => 0, () => true);
        PressFeedback.SetAnimator(wheel, animator);
        try
        {
            var petal = new WheelPetal(new("image.convert.png", "PNG", OperationMode.Convert, ".png", false, false), 0, 7);
            var retained = (List<WheelPetal>)typeof(WheelWindow).GetField("petals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel)!;
            retained.Add(petal); ((Canvas)wheel.FindName("Sectors")).Children.Add(petal);
            typeof(WheelWindow).GetMethod("SetMaterialState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wheel, [BackdropState.PendingFrosted]);
            petal.SetSelected(true, true);
            Assert.False(animator.HasChannel(petal, "specular"));
        }
        finally { wheel.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Optical_wheel_states_do_not_add_decorative_dark_rims(bool live) => desktop.RunAsync(() =>
    {
        var state = live ? BackdropState.LiveRefractiveGlass : BackdropState.PendingFrosted;
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            void Set(BackdropState value) => typeof(WheelWindow).GetMethod("SetMaterialState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wheel, [value]);
            Set(state);
            foreach (string key in Edges) Assert.Equal(0, Assert.IsType<SolidColorBrush>(wheel.FindResource(key)).Color.A);
            var navigation = (Border)wheel.FindName("NavigationPlate");
            Assert.Equal(0, Assert.IsType<SolidColorBrush>(navigation.BorderBrush).Color.A);
            Set(BackdropState.Solid);
            foreach (string key in Edges) Assert.Equal(0, Assert.IsType<SolidColorBrush>(wheel.FindResource(key)).Color.A);
        }
        finally { wheel.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Demo_optical_states_do_not_add_decorative_dark_rims(bool live) => desktop.RunAsync(() =>
    {
        var state = live ? GlassPresentationStage.Live : GlassPresentationStage.Pending;
        var demo = new WheelDemoWindow();
        try
        {
            void Set(GlassPresentationStage value) => typeof(WheelDemoWindow).GetMethod("SetGlassStage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(demo, [value]);
            Set(state);
            foreach (string key in Edges) Assert.Equal(0, Assert.IsType<SolidColorBrush>(demo.FindResource(key)).Color.A);
            Set(GlassPresentationStage.Fallback);
            foreach (string key in Edges) Assert.Equal(0, Assert.IsType<SolidColorBrush>(demo.FindResource(key)).Color.A);
        }
        finally { demo.Close(); }
        return Task.CompletedTask;
    });
}
