using System.IO;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Slider = System.Windows.Controls.Slider;
using Image = System.Windows.Controls.Image;
using Button = System.Windows.Controls.Button;
using Border = System.Windows.Controls.Border;
using Grid = System.Windows.Controls.Grid;
using TextBlock = System.Windows.Controls.TextBlock;
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
    public Task Successful_transport_retry_replaces_a_prior_UI_error_and_keeps_current_notices() => desktop.RunAsync(async () =>
    {
        await using var controller = new MediaPreviewController(new MediaPlaybackTests.Preparation { Silent = true }, () => new MediaPlaybackTests.Sink());
        var control = new MediaTransport { Controller = controller, VideoMode = true, ExternalTimeline = true };
        var window = new Window { Content = control, Width = 600, Height = 360, ShowInTaskbar = false };
        var recipe = new VideoRecipe([], null, new(1, 1), ImageFit.Contain, true, [], [], null, []);
        var document = new EditorDocument(1, 8, [], [], recipe, new("mp4", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
        bool fail = true; const string error = "Temporary frame navigation failure.";
        control.FrameRequested = _ => fail ? Task.FromException(new IOException(error)) : Task.CompletedTask;
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await control.LoadAsync(document, new(new(0, 1), new(2, 1)), default);
            Click(); await Wait(() => ((TextBlock)control.FindName("VideoStatus")).Text == error);
            Assert.Equal(MediaPlaybackState.Paused, controller.State);
            const string notice = "Preview is silent; the saved copy retains audio.";
            control.PreviewNotice = notice;
            fail = false; Click(); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.Equal(notice, ((TextBlock)control.FindName("VideoStatus")).Text);
            Assert.Equal(Visibility.Visible, ((Border)control.FindName("VideoStatusOverlay")).Visibility);
            fail = true; Click(); await Wait(() => ((TextBlock)control.FindName("VideoStatus")).Text == error);
            Assert.Equal(Visibility.Visible, ((Border)control.FindName("VideoStatusOverlay")).Visibility);
            void Click() => ((Button)control.FindName("Next")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Video_status_transitions_keep_picture_and_trim_bounds_stable_and_retain_full_messages() => desktop.RunAsync(async () =>
    {
        var preparation = new StatusPreparation();
        await using var controller = new MediaPreviewController(preparation, () => new MediaPlaybackTests.Sink());
        var trim = new VideoTrimSelectionView();
        var control = new MediaTransport { Controller = controller, VideoMode = true, ExternalTimeline = true, TimelineAdornment = trim };
        control.TimelineControl.Visibility = Visibility.Collapsed;
        var window = new Window { Content = control, Width = 640, Height = 360, ShowInTaskbar = false };
        var recipe = new VideoRecipe([], null, new(1, 1), ImageFit.Contain, true, [], [], null, []);
        var doc = new EditorDocument(1, 8, [], [], recipe, new("mp4", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
        var range = new TimeRange(new(0, 1), new(2, 1));
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await control.LoadAsync(doc, range, default); window.UpdateLayout();
            var picture = Assert.IsType<Grid>(control.FindName("Picture"));
            var originalPicture = Bounds(picture); var originalTrim = Bounds(trim);
            AssertStable();

            preparation.Block = true;
            using (var cancel = new CancellationTokenSource())
            {
                var preparing = control.LoadAsync(doc, range, cancel.Token);
                await Wait(() => controller.State == MediaPlaybackState.Preparing);
                await Dispatcher.Yield(DispatcherPriority.ContextIdle); AssertStable();
                Assert.Contains("Updating preview", Assert.IsType<TextBlock>(control.FindName("Status")).Text);
                cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparing);
            }
            preparation.Block = false;
            await control.LoadAsync(doc, range, default); AssertStable();
            const string notice = "Surround audio preview is silent. The saved copy keeps every original audio channel. More information remains available without moving the picture or trim handles.";
            control.PreviewNotice = notice; controller.Pause(); AssertStable(); AssertOverlay(notice);
            var redaction = new Citrus.Windows.Editors.CropOverlay { Plane = new(100, 100), Crop = new(5, 5, 90, 90) };
            picture.Children.Add(redaction); window.UpdateLayout();
            Assert.Same(redaction, picture.InputHitTest(new System.Windows.Point(picture.ActualWidth / 2, picture.ActualHeight * .95)));
            Assert.Equal(notice, picture.ToolTip);
            picture.Children.Remove(redaction);
            control.PreviewNotice = null; controller.Pause(); AssertStable();
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(control.FindName("VideoStatusOverlay")).Visibility);
            preparation.Fail = true;
            await Assert.ThrowsAsync<IOException>(() => control.LoadAsync(doc, range, default));
            await Dispatcher.Yield(DispatcherPriority.ContextIdle); AssertStable(); AssertOverlay(StatusPreparation.ErrorMessage);

            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(control).TransformBounds(new Rect(new System.Windows.Point(), element.RenderSize));
            void AssertStable() { window.UpdateLayout(); Assert.Equal(originalPicture, Bounds(picture)); Assert.Equal(originalTrim, Bounds(trim)); }
            void AssertOverlay(string message)
            {
                var overlay = Assert.IsType<Border>(control.FindName("VideoStatusOverlay"));
                var text = Assert.IsType<TextBlock>(control.FindName("VideoStatus"));
                Assert.Equal(Visibility.Visible, overlay.Visibility); Assert.Equal(message, text.Text); Assert.Equal(message, text.ToolTip);
                Assert.Equal(message, System.Windows.Automation.AutomationProperties.GetName(text));
                VisualArtifacts.AssertInside(overlay, picture);
                Assert.True(overlay.ActualHeight <= picture.ActualHeight && overlay.ActualWidth <= picture.ActualWidth);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Pausing_for_an_external_scrub_keeps_the_new_cursor_until_its_frame_is_ready() => desktop.RunAsync(async () =>
    {
        await using var controller = new MediaPreviewController(new MediaPlaybackTests.Preparation(), () => new MediaPlaybackTests.Sink());
        var control = new MediaTransport { Controller = controller, VideoMode = true, ExternalTimeline = true };
        var window = new Window { Content = control, Width = 600, Height = 360, ShowInTaskbar = false };
        var doc = new EditorDocument(1, 8, [], [], new AudioRecipe([], 48000, new(["FC"], ["FC"], [1], false), [], null, []),
            new("wav", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await control.LoadAsync(doc, new(new(0, 1), new(1, 1)), default);
            control.TimelineControl.Maximum = 30; control.TimelineControl.Value = 20;
            control.PauseForSeek();
            Assert.Equal(20, control.TimelineControl.Value);
            controller.Stop(); // Committing a snapshot/effect also stops the old decode before preparing the new cursor.
            Assert.Equal(20, control.TimelineControl.Value);
            Assert.Equal(new RationalTime(0, 1), controller.Position);
        }
        finally { window.Close(); }
    });

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
    public Task Reusable_transport_presents_exact_pixels_reuses_bitmap_and_stops_on_unload() => desktop.RunAsync(async () =>
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
            var audioStatus = Assert.IsType<TextBlock>(control.FindName("Status"));
            Assert.Equal(Visibility.Visible, audioStatus.Visibility); Assert.Equal(5, Grid.GetRow(audioStatus));
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(control.FindName("VideoStatusOverlay")).Visibility);
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(preview.Source);
            Assert.IsType<WriteableBitmap>(bitmap);
            byte[] pixels = new byte[4]; bitmap.CopyPixels(pixels, 4, 0);
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, pixels);
            await control.LoadAsync(doc, new(new(1, 10), new(2, 1)), default);
            Assert.Same(bitmap, preview.Source);
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

    [Fact]
    public Task Missing_audio_device_keeps_video_preview_playing_without_changing_the_saved_recipe() => desktop.RunAsync(async () =>
    {
        var preparation = new VideoPreparation();
        await using var controller = new MediaPreviewController(preparation, () => new MissingAudioSink());
        var control = new MediaTransport { Controller = controller, VideoMode = true };
        var window = new Window { Content = control, Width = 600, Height = 360, ShowInTaskbar = false };
        var clip = new MediaClip(Guid.NewGuid(), Guid.Empty, 0, 1, new(new(0, 1), new(2, 1)), 1);
        var recipe = new VideoRecipe([clip], null, new(1, 1), ImageFit.Contain, false, [], [], null, []);
        var doc = new EditorDocument(1, 8, [], [], recipe, new("mp4", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await control.LoadAsync(doc, new(new(0, 1), new(2, 1)), default);
            ((Button)control.FindName("Play")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (controller.State != MediaPlaybackState.Playing || !((System.Windows.Controls.TextBlock)control.FindName("Status")).Text.Contains("silently", StringComparison.Ordinal)) await Task.Delay(20, timeout.Token);
            Assert.Contains("silently", ((System.Windows.Controls.TextBlock)control.FindName("Status")).Text);
            Assert.Equal(Visibility.Visible, Assert.IsType<Border>(control.FindName("VideoStatusOverlay")).Visibility);
            Assert.NotNull(((Image)control.FindName("Preview")).Source);
            Assert.False(recipe.Mute); Assert.Equal(1, recipe.Clips[0].AudioStream);
            Assert.Contains(preparation.Documents, value => value.Recipe is VideoRecipe video && video.Clips.All(c => c.AudioStream is null));
        }
        finally { window.Close(); }
    });

    private sealed class VideoPreparation : IMediaPlaybackPreparation
    {
        public List<EditorDocument> Documents { get; } = [];
        public Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument document, TimeRange range, bool original, long generation, CancellationToken ct)
        {
            Documents.Add(document);
            bool silent = document.Recipe is VideoRecipe video && video.Clips.All(clip => clip.AudioStream is null);
            return Task.FromResult<IPreparedMediaPlayback>(new MediaPlaybackTests.Prepared(range, generation, silent, false));
        }
    }
    private sealed class StatusPreparation : IMediaPlaybackPreparation
    {
        public const string ErrorMessage = "The video preview could not be decoded. This detailed failure remains fully available to assistive technology and through the message tooltip while the picture keeps its original size.";
        public bool Block, Fail;
        public async Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument document, TimeRange range, bool original, long generation, CancellationToken ct)
        {
            if (Block) await Task.Delay(Timeout.Infinite, ct);
            if (Fail) throw new IOException(ErrorMessage);
            return new MediaPlaybackTests.Prepared(range, generation, true, false);
        }
    }
    private static async Task Wait(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private sealed class MissingAudioSink : IPreviewAudioSink
    {
        public RationalTime Position => new(0, 1);
        public Task Completion => Task.CompletedTask;
        public Task StartAsync(Citrus.Conversion.Editing.Media.AudioPcmInfo pcm, CancellationToken ct) => Task.FromException(new AudioDeviceUnavailableException("No render endpoint."));
        public void Stop() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
