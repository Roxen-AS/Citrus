using System.Collections.Immutable;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Media;

namespace Citrus.Windows.Tests;

public sealed class MediaPlaybackTeardownTests
{
    private static readonly TimeRange Range = new(new(1, 30), new(2, 1));
    private static EditorDocument Document => new(1, 8, [], [], new AudioRecipe([], 48000,
        new(["FC"], ["FC"], [1], false), [], null, []), new("wav", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_unload_stop_and_pause_after_disposal_are_idempotent(bool playing)
    {
        var preparation = new MediaPlaybackTests.Preparation();
        var sink = new MediaPlaybackTests.Sink();
        var controller = new MediaPreviewController(preparation, () => sink);
        if (playing) await controller.PlayAsync(Document, Range, false, default);
        else await controller.PreparePausedAsync(Document, Range, false, default);
        await controller.DisposeAsync();
        var generation = controller.Generation;
        var position = controller.Position;
        for (int i = 0; i < 3; i++) { controller.Stop(); controller.Pause(); await controller.DisposeAsync(); }
        Assert.Equal(generation, controller.Generation);
        Assert.Equal(position, controller.Position);
        Assert.Equal(MediaPlaybackState.Stopped, controller.State);
        Assert.False(controller.TryPresentFrame(_ => throw new Exception("Disposed frame escaped.")));
        Assert.All(preparation.Sessions, item => Assert.True(item.Disposed));
        if (playing) Assert.True(sink.Released);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => controller.PlayAsync(Document, Range, false, default));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => controller.SeekAsync(Range.Start, default));
    }

    [Fact]
    public async Task Concurrent_disposers_join_the_same_cleanup_and_late_stop_cannot_change_state()
    {
        var preparation = new GatedCleanup();
        var controller = new MediaPreviewController(preparation, () => new MediaPlaybackTests.Sink());
        await controller.PlayAsync(Document, Range, false, default);
        var first = controller.DisposeAsync().AsTask();
        await preparation.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = controller.DisposeAsync().AsTask();
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            var generation = controller.Generation;
            controller.Stop(); controller.Pause();
            Assert.Equal(generation, controller.Generation);
            Assert.Equal(MediaPlaybackState.Stopped, controller.State);
        }
        finally { preparation.ReleaseCleanup.TrySetResult(); await first; await second; }
        Assert.Equal(1, preparation.Disposals);
    }

    [Fact]
    public async Task Disposal_during_preparation_cancels_command_and_prevents_late_publication()
    {
        var preparation = new MediaPlaybackTests.Preparation { Block = true };
        int sinkCount = 0;
        var controller = new MediaPreviewController(preparation, () => { sinkCount++; return new MediaPlaybackTests.Sink(); });
        var preparing = controller.PreparePausedAsync(Document, Range, false, default);
        await preparation.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparing);
        controller.Stop();
        Assert.Equal(MediaPlaybackState.Stopped, controller.State);
        Assert.Equal(0, sinkCount);
        Assert.False(controller.TryPresentFrame(_ => throw new Exception("Late frame escaped.")));
    }

    private sealed class GatedCleanup : IMediaPlaybackPreparation
    {
        public readonly TaskCompletionSource CleanupEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals;
        public Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument document, TimeRange range, bool original, long generation, CancellationToken ct)
            => Task.FromResult<IPreparedMediaPlayback>(new Prepared(this, new(range, generation, false, false)));
        private sealed class Prepared(GatedCleanup owner, MediaPlaybackTests.Prepared inner) : IPreparedMediaPlayback
        {
            public TimeRange Range => inner.Range;
            public AudioPcmInfo? Audio => inner.Audio;
            public ImmutableArray<WaveformBucket> Waveform => inner.Waveform;
            public bool HasVideo => inner.HasVideo;
            public ImmutableArray<RationalTime> FrameTimes => inner.FrameTimes;
            public Task StreamVideoAsync(Func<PreviewFrame, CancellationToken, ValueTask> consume, CancellationToken ct) => inner.StreamVideoAsync(consume, ct);
            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.Disposals);
                owner.CleanupEntered.TrySetResult();
                await owner.ReleaseCleanup.Task;
                await inner.DisposeAsync();
            }
        }
    }
}
