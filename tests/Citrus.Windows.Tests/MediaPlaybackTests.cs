using System.IO;
using System.Collections.Immutable;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Media;

namespace Citrus.Windows.Tests;

/// <summary>Deterministic device seam tests. These do not claim real WASAPI output.</summary>
public sealed class MediaPlaybackTests
{
    private static readonly RationalTime Zero = new(0, 1);
    private static readonly TimeRange Range = new(Zero, new(2, 1));
    private static EditorDocument Document => new(1, 8, [], [], new AudioRecipe([], 48000,
        new(["FC"], ["FC"], [1], false), [], null, []), new("wav", null, "test", new QualityGoal(80), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
    [Fact]
    public async Task Pause_seek_flush_and_cancel_release_audio_device()
    {
        var preparation = new Preparation(); var sinks = new List<Sink>();
        await using var controller = new MediaPreviewController(preparation, () => { var s = new Sink(); sinks.Add(s); return s; });
        await controller.PlayAsync(Document, Range, false, default);
        sinks[0].At = new(1, 2);
        Assert.Equal(new RationalTime(1, 2), controller.Position);
        controller.Pause(); await controller.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(sinks[0].Released); Assert.True(preparation.Sessions[0].Disposed);
        Assert.Equal(MediaPlaybackState.Paused, controller.State);
        Assert.False(controller.TryPresentFrame(_ => { }));
        await controller.SeekAsync(new(3, 4), default);
        await controller.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(sinks); // Paused seek cannot briefly play audio.
        Assert.True(controller.TryPresentFrame(seek => Assert.Equal(new RationalTime(3, 4), seek.ActualTime)));
        Assert.False(controller.TryPresentFrame(_ => { }));
        await controller.ResumeAsync(default);
        Assert.Equal(2, sinks.Count);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.SeekAsync(new(1, 1), canceled.Token));
        await controller.WaitForIdleAsync();
        Assert.True(sinks[1].Released);
    }
    [Fact]
    public async Task A_v_clock_remains_bounded_under_ui_frame_drop()
    {
        var preparation = new Preparation(); var sink = new Sink();
        await using var controller = new MediaPreviewController(preparation, () => sink);
        await controller.PlayAsync(Document, Range, false, default);
        // Simulate a blocked dispatcher. Decode is backpressured, audio device clock keeps advancing.
        sink.At = new(7, 5);
        await Task.Delay(60);
        Assert.Equal(new RationalTime(7, 5), controller.Position);
        Assert.InRange(preparation.Sessions[0].CompletedWrites, 1, 1);
        RationalTime? actualTime = null;
        for (int i = 0; i < 40; i++)
        {
            controller.TryPresentFrame(next => actualTime = next.ActualTime);
            if (actualTime == new RationalTime(7, 5)) break;
            await Task.Delay(5);
        }
        Assert.NotNull(actualTime); Assert.Equal(new RationalTime(7, 5), actualTime);
        Assert.Equal(new RationalTime(7, 5), controller.Position); // UI never becomes the clock.
        controller.Stop(); await controller.WaitForIdleAsync(); Assert.True(sink.Released);
    }
    [Fact]
    public async Task Original_toggle_prepares_same_position_and_never_reuses_previous_pcm()
    {
        var preparation = new Preparation(); var sinks = new List<Sink>();
        await using var controller = new MediaPreviewController(preparation, () => { var sink = new Sink(); sinks.Add(sink); return sink; });
        await controller.PlayAsync(Document, Range, false, default);
        sinks[0].At = new(2, 5);
        string editedPath = sinks[0].Pcm!.Path;
        await controller.SetOriginalAsync(true, default);
        Assert.True(sinks[0].Released);
        Assert.True(preparation.Calls[^1].Original);
        Assert.Equal(new RationalTime(2, 5), preparation.Calls[^1].Range.Start);
        Assert.NotEqual(editedPath, sinks[1].Pcm!.Path);
        controller.Stop(); await controller.WaitForIdleAsync();
    }
    [Fact]
    public async Task Cancel_preparation_disposes_before_replacement_and_never_opens_sink()
    {
        var preparation = new Preparation { Block = true }; int created = 0;
        await using var controller = new MediaPreviewController(preparation, () => { created++; return new Sink(); });
        using var cancel = new CancellationTokenSource();
        var play = controller.PlayAsync(Document, Range, false, cancel.Token);
        await preparation.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MediaPlaybackState.Preparing, controller.State);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => play);
        await controller.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, created); Assert.NotEqual(MediaPlaybackState.Playing, controller.State);
        preparation.Block = false;
        await controller.PlayAsync(Document, Range, false, default);
        Assert.Equal(1, created); controller.Stop(); await controller.WaitForIdleAsync();
    }
    [Fact]
    public async Task Silent_video_clock_is_monotonic_and_pause_stops_position()
    {
        var preparation = new Preparation { Silent = true };
        await using var controller = new MediaPreviewController(preparation, () => throw new Exception("Silent preview must not open audio."));
        await controller.PlayAsync(Document, Range, false, default);
        var first = controller.Position; await Task.Delay(30); var second = controller.Position;
        Assert.True(second.CompareTo(first) > 0);
        controller.Pause(); var paused = controller.Position; await controller.WaitForIdleAsync(); await Task.Delay(30);
        Assert.Equal(paused, controller.Position);
    }
    [Fact]
    public async Task Decoder_failure_is_visible_and_releases_device_and_temp_session()
    {
        var preparation = new Preparation { Fail = true }; var sink = new Sink();
        await using var controller = new MediaPreviewController(preparation, () => sink);
        try { await controller.PlayAsync(Document, Range, false, default); } catch (IOException) { }
        await controller.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MediaPlaybackState.Failed, controller.State); Assert.IsType<IOException>(controller.Error);
        Assert.True(preparation.Sessions.Single().Disposed);
    }
    [Fact]
    public async Task Device_failure_is_visible_and_never_silently_uses_monotonic_audio_clock()
    {
        var preparation = new Preparation(); var sink = new Sink { Fail = true };
        await using var controller = new MediaPreviewController(preparation, () => sink);
        await Assert.ThrowsAsync<IOException>(() => controller.PlayAsync(Document, Range, false, default));
        await controller.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MediaPlaybackState.Failed, controller.State); Assert.True(sink.Released);
        Assert.True(preparation.Sessions.Single().Disposed);
    }
    [Fact]
    public async Task Frame_step_uses_actual_timestamp_list_and_never_fps_rounding()
    {
        var preparation = new Preparation();
        await using var controller = new MediaPreviewController(preparation, () => new Sink());
        await controller.PreparePausedAsync(Document, Range, false, default);
        await controller.WaitForIdleAsync();
        await controller.StepAsync(1, default); await controller.WaitForIdleAsync();
        Assert.Equal(new RationalTime(1, 25), controller.Position);
        await controller.StepAsync(-1, default); await controller.WaitForIdleAsync();
        Assert.Equal(Zero, controller.Position);
    }
    [Fact]
    public async Task Sink_teardown_failure_still_disposes_owned_preparation()
    {
        var preparation = new Preparation(); var sink = new Sink { StopFailure = true };
        await using var controller = new MediaPreviewController(preparation, () => sink);
        await controller.PlayAsync(Document, Range, false, default);
        controller.Pause();
        await controller.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(preparation.Sessions.Single().Disposed);
        Assert.IsType<IOException>(controller.Error);
        Assert.Equal(MediaPlaybackState.Failed, controller.State);
    }
    internal sealed class Preparation : IMediaPlaybackPreparation
    {
        public bool Block, Silent, Fail;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(TimeRange Range, bool Original)> Calls { get; } = [];
        public List<Prepared> Sessions { get; } = [];
        public async Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument doc, TimeRange range, bool original, long generation, CancellationToken ct)
        {
            Calls.Add((range, original)); Entered.TrySetResult();
            if (Block) await Task.Delay(Timeout.Infinite, ct);
            var session = new Prepared(range, generation, Silent, Fail); Sessions.Add(session); return session;
        }
    }
    internal sealed class Prepared(TimeRange range, long generation, bool silent, bool fail) : IPreparedMediaPlayback
    {
        public int CompletedWrites;
        public int FramesCreated;
        public bool Disposed;
        public TimeRange Range => range;
        public AudioPcmInfo? Audio { get; } = silent ? null : new(Guid.NewGuid() + ".pcm", 48000, 1, 96000);
        public ImmutableArray<WaveformBucket> Waveform => [new(-.2f, .3f, 0, 96000)];
        public bool HasVideo => true;
        public ImmutableArray<RationalTime> FrameTimes => [new(0, 1), new(1, 25), new(1, 10), new(23, 100), new(23, 50), .. Enumerable.Range(5, 15).Select(i => new RationalTime(i, 10))];
        public async Task StreamVideoAsync(Func<PreviewFrame, CancellationToken, ValueTask> consume, CancellationToken ct)
        {
            for (int i = 0; i < FrameTimes.Length; i++)
            {
                var offset = FrameTimes[i];
                var time = new RationalTime(range.Start.Numerator * offset.Denominator + offset.Numerator * range.Start.Denominator, range.Start.Denominator * offset.Denominator);
                if (time.CompareTo(range.End) >= 0) break;
                Interlocked.Increment(ref FramesCreated);
                await consume(new(8, generation, new(1, 1), 4, [1, 2, 3, 255], time, new(Guid.Empty, 0, i), []), ct);
                Interlocked.Increment(ref CompletedWrites);
                if (fail) throw new IOException("Decoder failed after the first frame.");
            }
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    internal sealed class Sink : IPreviewAudioSink
    {
        public RationalTime At = Zero;
        public bool Released, Fail, StopFailure;
        public AudioPcmInfo? Pcm;
        private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RationalTime Position => At;
        public Task Completion => ended.Task;
        public Task StartAsync(AudioPcmInfo pcm, CancellationToken ct)
        { Pcm = pcm; if (Fail) throw new IOException("Device invalidated."); return Task.CompletedTask; }
        public void Stop() { Released = true; ended.TrySetResult(); if (StopFailure) throw new IOException("Device teardown failed."); }
        public ValueTask DisposeAsync() { Stop(); return ValueTask.CompletedTask; }
    }
}
