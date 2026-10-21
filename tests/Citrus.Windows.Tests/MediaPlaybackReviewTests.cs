using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Media;

namespace Citrus.Windows.Tests;

public sealed class MediaPlaybackReviewTests
{
    private static EditorDocument Document => new(1, 8, [], [], new AudioRecipe([], 48000,
        new(["FC"], ["FC"], [1], false), [], null, []), new("wav", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
    private static readonly TimeRange Range = new(new(0, 1), new(2, 1));
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Replacement_during_paused_preparation_preserves_paused_intent(bool duringSeek, bool audition)
    {
        var preparation = new BlockingPreparation(); int sinks = 0;
        await using var controller = new MediaPreviewController(preparation, () => { sinks++; return new MediaPlaybackTests.Sink(); });
        if (duringSeek)
        {
            await controller.PreparePausedAsync(Document, Range, false, default);
            await controller.WaitForIdleAsync();
        }
        preparation.BlockNext = true;
        var paused = duringSeek ? controller.SeekAsync(new(1, 4), default) : controller.PreparePausedAsync(Document, Range, false, default);
        await preparation.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MediaPlaybackState.Preparing, controller.State);
        var replacement = audition ? controller.SetOriginalAsync(true, default) : controller.SeekAsync(new(1, 2), default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => paused);
        await replacement;
        Assert.Equal(0, sinks);
        Assert.Equal(MediaPlaybackState.Paused, controller.State);
        await controller.WaitForIdleAsync();
    }
    [Fact]
    public async Task Slow_presentation_handoff_counts_against_three_frame_budget()
    {
        var preparation = new MediaPlaybackTests.Preparation(); var sink = new MediaPlaybackTests.Sink();
        await using var controller = new MediaPreviewController(preparation, () => sink);
        await controller.PlayAsync(Document, Range, false, default);
        using var hold = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = Task.Run(() => controller.TryPresentFrame(frame =>
        {
            Assert.Equal(new RationalTime(0, 1), frame.ActualTime);
            entered.TrySetResult();
            Assert.True(hold.Wait(TimeSpan.FromSeconds(5)));
            GC.KeepAlive(frame);
        }));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(80); // producer may refill while the synchronous bitmap copy is stalled
            Assert.InRange(preparation.Sessions[0].FramesCreated, 1, 3);
            sink.At = new(1, 1);
            Assert.False(controller.TryPresentFrame(_ => throw new Exception("A second consumer must not acquire a payload.")));
            controller.Stop();
            await controller.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(sink.Released); Assert.True(preparation.Sessions[0].Disposed);
        }
        finally { hold.Set(); await presentation; }
        controller.Stop(); await controller.WaitForIdleAsync();
    }
    [Fact]
    public void Bitmap_copy_receives_existing_payload_without_managed_clone()
    {
        var pixels = ImmutableArray.Create(new byte[1024 * 1024 * 4]);
        var frame = new PreviewFrame(1, 1, new(1024, 1024), 4096, pixels, new(0, 1), new(Guid.NewGuid(), 0, 0), []);
        Assert.Same(ImmutableCollectionsMarshal.AsArray(pixels), MediaPresentation.Pixels(frame));
    }
    [Fact]
    public void Home_keeps_exact_non_microsecond_selection_start()
    {
        var range = new TimeRange(new(1, 30), new(2, 1));
        Assert.Equal(range.Start, MediaPresentation.SeekPosition(1d / 30, range));
        Assert.True(MediaPresentation.SeekPosition(1d / 30 + 0.00000001, range).CompareTo(range.Start) >= 0);
        Assert.Equal(range.End, MediaPresentation.SeekPosition(2d, range));
    }
    [Fact]
    public async Task Replacement_during_playing_preparation_preserves_play_intent()
    {
        var preparation = new BlockingPreparation { BlockNext = true }; int sinks = 0;
        await using var controller = new MediaPreviewController(preparation, () => { sinks++; return new MediaPlaybackTests.Sink(); });
        var play = controller.PlayAsync(Document, Range, false, default);
        await preparation.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.SeekAsync(new(1, 2), default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => play);
        Assert.Equal(1, sinks); Assert.Equal(MediaPlaybackState.Playing, controller.State);
        controller.Stop(); await controller.WaitForIdleAsync();
    }
    [Fact]
    public async Task Presentation_failure_releases_handoff_before_next_generation()
    {
        var preparation = new MediaPlaybackTests.Preparation();
        await using var controller = new MediaPreviewController(preparation, () => new MediaPlaybackTests.Sink());
        await controller.PreparePausedAsync(Document, Range, false, default);
        Assert.Throws<InvalidOperationException>(() => controller.TryPresentFrame(_ => throw new InvalidOperationException("Bitmap copy failed.")));
        await controller.SeekAsync(new(1, 2), default);
        Assert.True(controller.TryPresentFrame(frame => Assert.Equal(new RationalTime(1, 2), frame.ActualTime)));
        await controller.WaitForIdleAsync();
    }
    [Fact]
    public async Task Seek_cannot_add_another_presentation_owner_while_previous_copy_is_pending()
    {
        var preparation = new MediaPlaybackTests.Preparation();
        await using var controller = new MediaPreviewController(preparation, () => new MediaPlaybackTests.Sink());
        await controller.PreparePausedAsync(Document, Range, false, default);
        using var hold = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = Task.Run(() => controller.TryPresentFrame(frame =>
        {
            entered.TrySetResult(); Assert.True(hold.Wait(TimeSpan.FromSeconds(5))); GC.KeepAlive(frame);
        }));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await controller.SeekAsync(new(1, 2), default);
            Assert.False(controller.TryPresentFrame(_ => throw new Exception("Previous presentation still owns its payload.")));
            await controller.WaitForIdleAsync();
            Assert.All(preparation.Sessions, session => Assert.True(session.Disposed));
        }
        finally { hold.Set(); await presentation; }
        Assert.True(controller.TryPresentFrame(frame => Assert.Equal(new RationalTime(1, 2), frame.ActualTime)));
    }
    private sealed class BlockingPreparation : IMediaPlaybackPreparation
    {
        public bool BlockNext;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MediaPlaybackTests.Preparation inner = new();
        public async Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument doc, TimeRange range, bool original, long generation, CancellationToken ct)
        {
            if (BlockNext) { BlockNext = false; Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            return await inner.PrepareAsync(doc, range, original, generation, ct);
        }
    }
}
