using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Slider = System.Windows.Controls.Slider;
using Image = System.Windows.Controls.Image;
using Button = System.Windows.Controls.Button;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Media;
using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class MediaTransportPresentationTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Disposed_owner_tolerates_actual_unload_reload_and_controller_replacement(bool playing) => desktop.RunAsync(async () =>
    {
        var control = new MediaTransport();
        var window = new Window { Width = 800, Height = 420, ShowInTaskbar = false };
        var doc = new EditorDocument(1, 8, [], [], new AudioRecipe([], 48000, new(["FC"], ["FC"], [1], false), [], null, []),
            new("wav", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
        int unloads = 0;
        control.Unloaded += (_, _) => unloads++;
        try
        {
            window.Show();
            for (int repeat = 0; repeat < 3; repeat++)
            {
                var preparation = new MediaPlaybackTests.Preparation();
                var sink = new MediaPlaybackTests.Sink();
                await using var controller = new MediaPreviewController(preparation, () => sink);
                control.Controller = controller;
                window.Content = control;
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Assert.True(control.IsLoaded);
                await control.LoadAsync(doc, new(new(1, 30), new(2, 1)), default);
                if (playing) await controller.ResumeAsync(default);
                await controller.DisposeAsync();
                long generation = controller.Generation;
                // Ownership ends before the real WPF render-priority Unloaded event arrives.
                window.Content = null;
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Assert.False(control.IsLoaded);
                Assert.Equal(repeat * 2 + 1, unloads);
                Assert.Equal(generation, controller.Generation);
                Assert.Equal(MediaPlaybackState.Stopped, controller.State);
                Assert.All(preparation.Sessions, item => Assert.True(item.Disposed));
                if (playing) Assert.True(sink.Released);

                // Attach the disposed owner again, then replace it while still loaded.
                // Detach must be safe even when Unloaded has not run for this attachment.
                window.Content = control;
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                await using var replacement = new MediaPreviewController(new MediaPlaybackTests.Preparation(), () => new MediaPlaybackTests.Sink());
                control.Controller = replacement;
                await control.LoadAsync(doc, new(new(1, 30), new(2, 1)), default);
                Assert.Equal(MediaPlaybackState.Paused, replacement.State);
                Assert.Equal(new RationalTime(1, 30), replacement.Position);
                window.Content = null;
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                await replacement.WaitForIdleAsync();
                Assert.Equal(MediaPlaybackState.Stopped, replacement.State);
                // Two real unloads occurred in this iteration.
                Assert.Equal((repeat + 1) * 2, unloads);
            }
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ContextIdle); }
    });

    [Fact]
    public Task Home_key_seeks_to_exact_one_thirtieth_start() => desktop.RunAsync(async () =>
    {
        var preparation = new MediaPlaybackTests.Preparation();
        await using var controller = new MediaPreviewController(preparation, () => new MediaPlaybackTests.Sink());
        var control = new MediaTransport { Controller = controller };
        var window = new Window { Content = control, Width = 800, Height = 420, ShowInTaskbar = false };
        var doc = new EditorDocument(1, 8, [], [], new AudioRecipe([], 48000, new(["FC"], ["FC"], [1], false), [], null, []),
            new("wav", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
        var start = new RationalTime(1, 30);
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await control.LoadAsync(doc, new(start, new(2, 1)), default);
            await controller.SeekAsync(new(1, 2), default);
            await controller.WaitForIdleAsync();
            var timeline = Assert.IsType<Slider>(control.FindName("Timeline"));
            timeline.Value = timeline.Minimum;
            timeline.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Home)
                { RoutedEvent = Keyboard.PreviewKeyUpEvent });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (controller.Position != start || controller.State != MediaPlaybackState.Paused)
                await Task.Delay(10, timeout.Token);
            Assert.Equal(start, controller.Position);
            Assert.Equal(start, preparation.Calls[^1].Range.Start);
            Assert.DoesNotContain("outside", Assert.IsType<System.Windows.Controls.TextBlock>(control.FindName("Status")).Text, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task Reusable_transport_presents_frozen_exact_pixels_consumes_press_style_and_stops_on_unload() => desktop.RunAsync(async () =>
    {
        var preparation = new MediaPlaybackTests.Preparation();
        var sinks = new List<MediaPlaybackTests.Sink>();
        await using var controller = new MediaPreviewController(preparation, () => { var sink = new MediaPlaybackTests.Sink(); sinks.Add(sink); return sink; });
        var control = new MediaTransport { Controller = controller };
        var window = new Window { Content = control, Width = 800, Height = 420, ShowInTaskbar = false };
        var doc = new EditorDocument(1, 8, [], [], new AudioRecipe([], 48000, new(["FC"], ["FC"], [1], false), [], null, []),
            new("wav", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await control.LoadAsync(doc, new(new(0, 1), new(2, 1)), default);
            var preview = Assert.IsType<Image>(control.FindName("Preview"));
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(preview.Source);
            Assert.True(bitmap.IsFrozen);
            byte[] pixels = new byte[4]; bitmap.CopyPixels(pixels, 4, 0);
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, pixels);
            Assert.True(PressFeedback.GetIsEnabled(Assert.IsType<Button>(control.FindName("Play"))));
            await controller.ResumeAsync(default);
            Assert.Single(sinks);
            window.Content = null;
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await controller.WaitForIdleAsync();
            Assert.Equal(MediaPlaybackState.Stopped, controller.State);
            Assert.True(sinks.Single().Released);
            Assert.All(preparation.Sessions, session => Assert.True(session.Disposed));
        }
        finally { window.Close(); }
    });
}
