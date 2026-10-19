using System.Windows;
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
