using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Citrus.Core;
using Citrus.Windows.Presentation;
using ShapePath = System.Windows.Shapes.Path;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using SystemColors = System.Windows.SystemColors;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class SingleBackingForegroundTests(DesktopFixture desktop)
{
    [Fact]
    public Task Petals_have_a_translucent_white_gradient_and_no_contour_stroke() => desktop.RunAsync(() =>
    {
        var petal = Petal();
        var surfaces = petal.Children.OfType<ShapePath>().Take(2).ToArray();
        var fill = Assert.IsType<LinearGradientBrush>(surfaces[0].Fill);
        Assert.Equal(new Point(0, 0), fill.StartPoint);
        Assert.Equal(new Point(0, 1), fill.EndPoint);
        Assert.Equal(Color.FromArgb(230, 255, 255, 255), fill.GradientStops[0].Color);
        Assert.Equal(Color.FromArgb(217, 241, 244, 244), fill.GradientStops[^1].Color);
        Assert.Equal(0, surfaces[0].StrokeThickness);
        Assert.All(surfaces, path => Assert.True(path.Stroke is null || path.Stroke.Opacity == 0 ||
            path.Stroke is SolidColorBrush solid && solid.Color.A == 0));
        Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(petal.Children.OfType<TextBlock>().Single().Foreground).Color);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Selection_is_solid_orange_immediately_without_a_glass_frame_or_specular_animation() => desktop.RunAsync(() =>
    {
        var petal = Petal();
        using var animator = new SpringAnimator();
        PressFeedback.SetAnimator(petal, animator);
        petal.SetSelected(true, true);
        var selected = petal.Children.OfType<ShapePath>().Where(path => path.Data is StreamGeometry).Skip(1).First();
        Assert.Equal(1, selected.Opacity);
        Assert.Equal(Color.FromRgb(255, 85, 0), Assert.IsType<SolidColorBrush>(selected.Fill).Color);
        Assert.Equal(255, Assert.IsType<SolidColorBrush>(selected.Fill).Color.A);
        Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(petal.Children.OfType<TextBlock>().Single().Foreground).Color);
        Assert.False(animator.HasChannel(petal, "specular"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Desktop_frames_do_not_change_pale_petal_ink_or_add_label_halos() => desktop.RunAsync(() =>
    {
        var petal = Petal();
        var label = petal.Children.OfType<TextBlock>().Single();
        var scene = GlassScene.Wheel(7, 1);
        var pixels = new byte[scene.PixelWidth * scene.PixelHeight * 4];
        for (int index = 0; index < pixels.Length; index += 4) pixels[index + 3] = 255;
        petal.ApplyGlassContrast(scene, pixels);
        Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
        Assert.Null(label.Effect);
        petal.SetSelected(true, false);
        petal.ApplyGlassContrast(scene, pixels);
        Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
        Assert.Null(label.Effect);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Pale_foreground_uses_system_surface_ink_and_selection_in_high_contrast() => desktop.RunAsync(() =>
    {
        var petal = Petal();
        petal.ApplyForegroundPalette(true);
        var surfaces = petal.Children.OfType<ShapePath>().Take(2).ToArray();
        var label = petal.Children.OfType<TextBlock>().Single();
        Assert.Same(SystemColors.WindowBrush, surfaces[0].Fill);
        Assert.Same(SystemColors.WindowTextBrush, surfaces[0].Stroke);
        Assert.Equal(1, surfaces[0].StrokeThickness);
        Assert.Same(SystemColors.WindowTextBrush, label.Foreground);
        petal.SetSelected(true, false);
        Assert.Same(SystemColors.HighlightBrush, surfaces[1].Fill);
        Assert.Same(SystemColors.HighlightTextBrush, label.Foreground);
        petal.ApplyForegroundPalette(false);
        Assert.Equal(0, surfaces[0].StrokeThickness);
        Assert.Same(WheelForegroundStyle.Petal, surfaces[0].Fill);
        Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Real_wheel_foreground_does_not_swap_between_pending_live_and_fallback(bool dark) => desktop.RunAsync(() =>
    {
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            typeof(WheelWindow).GetField("dark", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(wheel, dark);
            Brush? fill = null, hub = null, selected = null;
            foreach (var state in new[] { BackdropState.PendingFrosted, BackdropState.LiveRefractiveGlass, BackdropState.Solid })
            {
                typeof(WheelWindow).GetMethod("SetMaterialState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wheel, [state]);
                var currentFill = Assert.IsType<LinearGradientBrush>(wheel.FindResource("PetalBackground"));
                var currentHub = Assert.IsType<SolidColorBrush>(wheel.FindResource("WheelHubBackground"));
                var currentSelected = Assert.IsType<SolidColorBrush>(wheel.FindResource("SelectedPetal"));
                fill ??= currentFill; hub ??= currentHub; selected ??= currentSelected;
                Assert.Same(fill, currentFill); Assert.Same(hub, currentHub); Assert.Same(selected, currentSelected);
                Assert.Equal(Color.FromArgb(240, 255, 255, 255), currentHub.Color);
                Assert.Equal(Color.FromRgb(255, 85, 0), currentSelected.Color);
                Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(wheel.FindResource("PrimaryText")).Color);
            }
        }
        finally { wheel.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Demonstration_keeps_the_same_foreground_while_its_backing_starts() => desktop.RunAsync(() =>
    {
        var demo = new WheelDemoWindow();
        try
        {
            Brush? fill = null;
            foreach (var stage in new[] { GlassPresentationStage.Pending, GlassPresentationStage.Live, GlassPresentationStage.Fallback })
            {
                typeof(WheelDemoWindow).GetMethod("SetGlassStage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(demo, [stage]);
                var current = Assert.IsType<LinearGradientBrush>(demo.FindResource("PetalBackground"));
                fill ??= current;
                Assert.Same(fill, current);
                Assert.Equal(Color.FromRgb(255, 85, 0), Assert.IsType<SolidColorBrush>(demo.FindResource("SelectedPetal")).Color);
            }
        }
        finally { demo.Close(); }
        return Task.CompletedTask;
    });

    private static WheelPetal Petal() => new(new("image.convert.png", "PNG", OperationMode.Convert, ".png", false, false), 0, 7);
}
