using System.Collections.Immutable;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Media;

namespace Citrus.Windows.Tests;

public sealed class MediaPosterPreparationTests
{
    private static readonly TimeRange Range = new(new(0, 1), new(2, 1));
    private static EditorDocument Video => new(1, 8, [], [], new VideoRecipe([], null, new(1, 1), ImageFit.Contain, false, [], [], null, []),
        new("mp4", null, "poster", new QualityGoal(80), MetadataPolicy.StripAll, new()));

    [Fact]
    public async Task Paused_video_presents_before_audio_preparation_and_never_opens_a_sink()
    {
        var preparation = new PosterPreparation();
        await using var controller = new MediaPreviewController(preparation, () => throw new Exception("A paused poster cannot open an audio device."));
        await controller.PreparePausedAsync(Video, Range, false, default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, preparation.PlaybackCalls);
        Assert.Equal(1, preparation.PosterCalls);
        Assert.Equal(MediaPlaybackState.Paused, controller.State);
        Assert.True(controller.TryPresentFrame(frame => Assert.Equal(Range.Start, frame.ActualTime)));
        Assert.Empty(controller.Waveform);
    }

    [Fact]
    public async Task New_paused_seek_cancels_old_poster_without_starting_pcm_work()
    {
        var preparation = new PosterPreparation();
        await using var controller = new MediaPreviewController(preparation, () => throw new Exception("Paused seek opened audio."));
        await controller.PreparePausedAsync(Video, Range, false, default);
        preparation.BlockPoster = true;
        var obsolete = controller.SeekAsync(new(1, 4), default);
        await preparation.PosterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        preparation.BlockPoster = false;
        await controller.SeekAsync(new(3, 4), default).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obsolete);
        Assert.Equal(0, preparation.PlaybackCalls);
        Assert.True(controller.TryPresentFrame(frame => Assert.Equal(new RationalTime(3, 4), frame.ActualTime)));
    }

    [Fact]
    public async Task Audio_is_prepared_on_explicit_play_and_session_cleanup_is_joined()
    {
        var preparation = new PosterPreparation();
        int sinks = 0;
        var controller = new MediaPreviewController(preparation, () => { sinks++; return new MediaPlaybackTests.Sink(); });
        await controller.PreparePausedAsync(Video, Range, false, default);
        await controller.ResumeAsync(default);
        Assert.Equal(1, preparation.PlaybackCalls);
        Assert.Equal(1, sinks);
        await controller.DisposeAsync();
        await controller.DisposeAsync();
        Assert.Equal(1, preparation.Disposals);
    }

    [Fact]
    public async Task Long_source_seeks_keep_new_neighbor_metadata_in_the_bounded_step_cache()
    {
        var preparation=new LongPosterPreparation();
        await using var controller=new MediaPreviewController(preparation,()=>throw new Exception("Paused step opened audio."));
        await controller.PreparePausedAsync(Video,new(new(0,1),new(40000,1)),false,default);
        await controller.SeekAsync(new(35000,1),default);
        await controller.StepAsync(1,default);
        Assert.Equal(new RationalTime(35001,1),controller.Position);
        await controller.StepAsync(-1,default);
        Assert.Equal(new RationalTime(35000,1),controller.Position);
    }

    private sealed class PosterPreparation : IMediaPlaybackPreparation, IPausedMediaPlaybackPreparation, IAsyncDisposable
    {
        public int PosterCalls, PlaybackCalls, Disposals;
        public bool BlockPoster;
        public TaskCompletionSource PosterEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument document, TimeRange range, bool original, long generation, CancellationToken ct)
        {
            Interlocked.Increment(ref PlaybackCalls);
            return Task.FromResult<IPreparedMediaPlayback>(new MediaPlaybackTests.Prepared(range, generation, false, false));
        }
        public async Task<IPreparedMediaPlayback> PreparePausedAsync(EditorDocument document, TimeRange range, bool original, long generation, CancellationToken ct)
        {
            Interlocked.Increment(ref PosterCalls);
            if (BlockPoster) { PosterEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            return new Poster(new MediaPlaybackTests.Prepared(range, generation, true, false));
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Poster(MediaPlaybackTests.Prepared inner) : IPreparedMediaPlayback
    {
        public TimeRange Range => inner.Range;
        public AudioPcmInfo? Audio => null;
        public ImmutableArray<WaveformBucket> Waveform => [];
        public bool HasVideo => true;
        public ImmutableArray<RationalTime> FrameTimes => inner.FrameTimes;
        public Task StreamVideoAsync(Func<PreviewFrame, CancellationToken, ValueTask> consume, CancellationToken ct) => inner.StreamVideoAsync(consume, ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class LongPosterPreparation : IMediaPlaybackPreparation,IPausedMediaPlaybackPreparation
    {
        public Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument document,TimeRange range,bool original,long generation,CancellationToken ct)=>throw new Exception("Paused step prepared playback.");
        public Task<IPreparedMediaPlayback> PreparePausedAsync(EditorDocument document,TimeRange range,bool original,long generation,CancellationToken ct)
        {
            var times=range.Start.Numerator==0?Enumerable.Range(0,32768).Select(i=>new RationalTime(i,1)).ToImmutableArray()
                :new[]{-1,0,1}.Select(i=>new RationalTime(range.Start.Numerator+i,1)).ToImmutableArray();
            return Task.FromResult<IPreparedMediaPlayback>(new ExactPoster(range,generation,times));
        }
    }
    private sealed class ExactPoster(TimeRange range,long generation,ImmutableArray<RationalTime> times) : IPreparedMediaPlayback
    {
        public TimeRange Range=>range;
        public AudioPcmInfo? Audio=>null;
        public ImmutableArray<WaveformBucket> Waveform=>[];
        public bool HasVideo=>true;
        public ImmutableArray<RationalTime> FrameTimes=>times;
        public async Task StreamVideoAsync(Func<PreviewFrame,CancellationToken,ValueTask> consume,CancellationToken ct)
            =>await consume(new(8,generation,new(1,1),4,[1,2,3,255],range.Start,new(Guid.Empty,0,range.Start.Numerator),[]),ct);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
