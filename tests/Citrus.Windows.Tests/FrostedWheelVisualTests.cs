using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Citrus.Core;
using Citrus.Core.Presentation;
using Citrus.Windows.Presentation;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class FrostedWheelVisualTests(DesktopFixture desktop)
{
    [Fact]
    public Task Optical_selection_does_not_add_a_second_sharp_white_outline() => desktop.RunAsync(() =>
    {
        var operation = new OperationDescriptor("image.convert.png", "PNG", OperationMode.Convert, ".png", false, false);
        var petal = new WheelPetal(operation, 0, 7);
        using var animator = new SpringAnimator();
        PressFeedback.SetAnimator(petal, animator);
        petal.SetSelected(true, true);
        Assert.False(animator.HasChannel(petal, "specular"));
        var scene = GlassScene.Wheel(7, 1);
        petal.ApplyGlassContrast(scene, FlatPixels(scene, 210));
        Assert.False(animator.HasChannel(petal, "specular"));
        petal.SetSelected(false, true); petal.SetSelected(true, true);
        Assert.False(animator.HasChannel(petal, "specular"));
        petal.ClearGlassContrast(); petal.SetSelected(true, true);
        Assert.False(animator.HasChannel(petal, "specular"));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Pale_petal_labels_keep_fixed_ink_without_a_filled_backing_element(bool format) => desktop.RunAsync(() =>
    {
        var operation = format
            ? new OperationDescriptor("image.convert.png", "PNG", OperationMode.Convert, ".png", false, false)
            : new OperationDescriptor("image.resize", "Resize image", OperationMode.Tools, null, false, false);
        var petal = new WheelPetal(operation, 0, 7);
        petal.ApplyTextScale(1.5);
        var label = petal.Children.OfType<TextBlock>().Single();
        var original = (label.Text, label.FontSize, label.FontWeight, label.Width, Canvas.GetLeft(label), Canvas.GetTop(label));
        var scene = GlassScene.Wheel(7, 1);

        foreach (byte channel in new byte[] { 0, 255 })
        {
            petal.ApplyGlassContrast(scene, FlatPixels(scene, channel));
            Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
            Assert.Null(label.Effect);
            Assert.DoesNotContain(petal.Children.Cast<UIElement>(), child => child is Border);
            Assert.Null(label.Background);
            Assert.Equal(original, (label.Text, label.FontSize, label.FontWeight, label.Width, Canvas.GetLeft(label), Canvas.GetTop(label)));
        }
        petal.ClearGlassContrast();
        Assert.DoesNotContain(petal.Children.Cast<UIElement>(), child => child is Border);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Dense_backdrop_does_not_add_a_second_label_effect_over_the_frosted_petal() => desktop.RunAsync(() =>
    {
        var operation = new OperationDescriptor("image.resize", "Resize image", OperationMode.Tools, null, false, false);
        var petal = new WheelPetal(operation, 0, 7);
        var scene = GlassScene.Wheel(7, 1);
        var pixels = FlatPixels(scene, 0);
        for (int y = 0; y < scene.PixelHeight; y++)
        for (int x = 0; x < scene.PixelWidth; x++)
        {
            int offset = (y * scene.PixelWidth + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = x % 2 == 0 ? (byte)0 : (byte)255;
        }
        petal.ApplyGlassContrast(scene, pixels);
        var label = petal.Children.OfType<TextBlock>().Single();
        Assert.Null(label.Effect);
        Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
        Assert.Null(label.Background);
        Assert.DoesNotContain(petal.Children.Cast<UIElement>(), child => child is Border);

        petal.ApplyGlassContrast(scene, FlatPixels(scene, 255));
        Assert.Null(label.Effect);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Demo_has_one_selected_caption_in_the_hub_and_none_below_the_wheel() => desktop.RunAsync(async () =>
    {
        var demo = new WheelDemoWindow();
        try
        {
            demo.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var canvas = Descendants(demo).OfType<Canvas>().Single(value => value.Name == "DemoWheelCanvas");
            var captions = Descendants(canvas).OfType<TextBlock>().Where(label => label.Text == "PNG").ToArray();
            Assert.Equal(2, captions.Length); // The petal's format and the retained center caption.
            Assert.DoesNotContain(Descendants(canvas).OfType<TextBlock>(), label => label.Text.StartsWith("Convert to ", StringComparison.Ordinal));
            Assert.DoesNotContain(canvas.Children.OfType<FrameworkElement>(), child => Canvas.GetTop(child) >= 368);
        }
        finally { demo.Close(); await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
    });

    [Fact]
    public Task Demo_consumes_right_down_and_right_up_before_closing() => desktop.RunAsync(async () =>
    {
        var demo = new WheelDemoWindow();
        try
        {
            demo.Show();
            var down = RightButton(UIElement.PreviewMouseRightButtonDownEvent);
            demo.RaiseEvent(down);
            Assert.True(down.Handled);
            Assert.True(demo.IsVisible);
            Assert.Same(demo, Mouse.Captured);

            var up = RightButton(UIElement.PreviewMouseRightButtonUpEvent);
            demo.RaiseEvent(up);
            Assert.True(up.Handled);
            Assert.False(demo.IsVisible);
            Assert.NotSame(demo, Mouse.Captured);
        }
        finally { demo.Close(); await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
    });

    [Theory]
    [InlineData(false, 225, 232, 232, 173)]
    [InlineData(true, 225, 232, 232, 173)]
    public Task Demo_keeps_pale_foreground_while_waiting_for_its_backing_frame(bool dark, byte red, byte green, byte blue, byte alpha) => desktop.RunAsync(async () =>
    {
        var frames = new FrameGate();
        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var demo = new WheelDemoWindow((scene, _, _) =>
        {
            var session = new ControlledSession(scene, frames);
            created.TrySetResult();
            return session;
        }, _ => new Lease());
        try
        {
            demo.ApplyMaterialPolicy(new(true, false, "Owned pending material policy"), dark);
            demo.Show();
            await created.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var shell = Descendants(demo).OfType<MaterialSurface>().Single(surface => surface.Role == MaterialRole.WheelShell);
            Assert.Equal(BackdropState.PendingFrosted, shell.State);
            Assert.Equal(Color.FromArgb(alpha, red, green, blue), Assert.IsType<SolidColorBrush>(shell.EffectiveTint).Color);
            Assert.All(Descendants(demo).OfType<Image>(), image => Assert.Null(image.Source));
            var retainedFill = Assert.IsType<LinearGradientBrush>(demo.Resources["PetalBackground"]);
            Assert.Equal(230, retainedFill.GradientStops[0].Color.A);

            frames.HasFrame = true;
            await WaitUntil(() => Descendants(demo).OfType<Image>().Any(image => image.Source is not null));
            Assert.Equal(BackdropState.LiveRefractiveGlass, shell.State);
            Assert.Same(retainedFill, demo.Resources["PetalBackground"]);
            demo.Hide();
            await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(BackdropState.Solid, shell.State);
            Assert.Same(retainedFill, demo.Resources["PetalBackground"]);
        }
        finally { demo.Close(); await demo.GlassCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
    });

    private static MouseButtonEventArgs RightButton(RoutedEvent routedEvent) =>
        new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = routedEvent };

    private static byte[] FlatPixels(GlassScene scene, byte channel)
    {
        var pixels = new byte[scene.PixelWidth * scene.PixelHeight * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        { pixels[i] = pixels[i + 1] = pixels[i + 2] = channel; pixels[i + 3] = 255; }
        return pixels;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class Lease : IDisposable { public void Dispose() { } }
    private sealed class FrameGate { public volatile bool HasFrame; }
    private sealed class ControlledSession(GlassScene scene, FrameGate frames) : ILiveGlassSession
    {
        public GlassScene Scene { get; } = scene;
        public bool TryRenderFrame(GlassFrameState state, byte[] destination)
        {
            if (!frames.HasFrame) return false;
            Array.Fill(destination, (byte)255);
            return true;
        }
        public void Dispose() { }
    }
}
