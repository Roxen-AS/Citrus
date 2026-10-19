using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Citrus.Conversion;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Media;

public interface IEditorPlayback
{
    Task PlayAsync(EditorDocument document, TimeRange range, bool original, CancellationToken ct);
    void Pause();
    Task SeekAsync(RationalTime position, CancellationToken ct);
    void Stop();
    RationalTime Position { get; }
}
public enum MediaPlaybackState { Stopped, Preparing, Playing, Paused, Failed }

/// <summary>Device position is elapsed presentation time, not bytes submitted to its buffer.</summary>
public interface IPreviewAudioSink : IAsyncDisposable
{
    Task StartAsync(AudioPcmInfo pcm, CancellationToken ct);
    RationalTime Position { get; }
    Task Completion { get; }
    void Stop();
}
public interface IPreparedMediaPlayback : IAsyncDisposable
{
    TimeRange Range { get; }
    AudioPcmInfo? Audio { get; }
    ImmutableArray<WaveformBucket> Waveform { get; }
    bool HasVideo { get; }
    ImmutableArray<RationalTime> FrameTimes { get; }
    Task StreamVideoAsync(Func<PreviewFrame, CancellationToken, ValueTask> consume, CancellationToken ct);
}
public interface IMediaPlaybackPreparation
{
    Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument document, TimeRange range, bool original, long generation, CancellationToken ct);
}

/// <summary>Session-local transport. Events run on worker threads; UI consumes frames with TryTakeFrame,
/// never posts a dispatcher work item per frame. Pause/seek invalidate the generation and flush pixels.
/// Await WaitForIdleAsync/DisposeAsync to join child termination and staging cleanup.</summary>
public sealed class MediaPreviewController : IEditorPlayback, IAsyncDisposable
{
    private readonly IMediaPlaybackPreparation preparation;
    private readonly Func<IPreviewAudioSink> sinkFactory;
    private readonly object sync = new();
    private readonly SemaphoreSlim commands = new(1, 1);
    private readonly Stopwatch silentClock = new();
    private CancellationTokenSource? active;
    private Task running = Task.CompletedTask;
    private IPreviewAudioSink? sink;
    private Channel<PreviewFrame>? frames;
    private EditorDocument? document;
    private TimeRange selection;
    private bool original, disposed;
    private long generation;
    private RationalTime position = new(0, 1), start = new(0, 1), end = new(0, 1);
    private MediaPlaybackState state;
    private ImmutableArray<RationalTime> frameTimes = [];
    public MediaPreviewController(IMediaPlaybackPreparation preparation, Func<IPreviewAudioSink> sinkFactory)
    { this.preparation = preparation; this.sinkFactory = sinkFactory; }
    public event EventHandler? StateChanged;
    public MediaPlaybackState State { get { lock (sync) return state; } }
    private Exception? error;
    private ImmutableArray<WaveformBucket> waveform = [];
    private TimeRange waveformRange = new(new(0, 1), new(0, 1));
    public Exception? Error { get { lock (sync) return error; } private set { lock (sync) error = value; } }
    public ImmutableArray<WaveformBucket> Waveform { get { lock (sync) return waveform; } private set { lock (sync) waveform = value; } }
    public TimeRange WaveformRange { get { lock (sync) return waveformRange; } private set { lock (sync) waveformRange = value; } }
    public long Generation { get { lock (sync) return generation; } }
    public RationalTime Position
    {
        get
        {
            lock (sync)
            {
                if (state != MediaPlaybackState.Playing) return position;
                var elapsed = sink?.Position ?? new RationalTime(silentClock.Elapsed.Ticks, 10_000_000);
                return Minimum(AddClock(start, elapsed), end);
            }
        }
    }
    public Task PlayAsync(EditorDocument document, TimeRange range, bool original, CancellationToken ct) => StartAsync(document, range, original, false, ct);
    public Task PreparePausedAsync(EditorDocument document, TimeRange range, bool original, CancellationToken ct) => StartAsync(document, range, original, true, ct);
    private async Task StartAsync(EditorDocument document, TimeRange range, bool original, bool paused, CancellationToken ct)
    {
        long ticket = StopCurrent(MediaPlaybackState.Stopped);
        await commands.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WaitForIdleAsync().ConfigureAwait(false);
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (ticket != generation) throw new OperationCanceledException("Superseded playback request.");
                this.document = document; selection = range; this.original = original; frameTimes = [];
            }
            await BeginAsync(range, ct, paused).ConfigureAwait(false);
        }
        finally { commands.Release(); }
    }
    public void Pause() => StopCurrent(MediaPlaybackState.Paused);
    public void Stop() => StopCurrent(MediaPlaybackState.Stopped);
    public Task SeekAsync(RationalTime position, CancellationToken ct) => SeekCoreAsync(position, null, null, ct);
    public Task ResumeAsync(CancellationToken ct) => SeekCoreAsync(Position, true, null, ct);
    public Task SetOriginalAsync(bool value, CancellationToken ct) => SeekCoreAsync(Position, null, value, ct);
    public Task StepAsync(int direction, CancellationToken ct)
    {
        if (direction is not -1 and not 1) throw new ArgumentOutOfRangeException(nameof(direction));
        RationalTime target;
        lock (sync)
        {
            if (frameTimes.IsEmpty) throw new NotSupportedException("Prepare a video selection before frame stepping.");
            int current = -1;
            for (int i = 0; i < frameTimes.Length && frameTimes[i].CompareTo(Position) <= 0; i++) current = i;
            int index = Math.Clamp(current + direction, 0, frameTimes.Length - 1);
            target = frameTimes[index].CompareTo(selection.Start) < 0 ? selection.Start : frameTimes[index];
        }
        Pause();
        return SeekCoreAsync(target, false, null, ct);
    }
    private async Task SeekCoreAsync(RationalTime position, bool? play, bool? audition, CancellationToken ct)
    {
        EditorDocument selected;
        bool resume;
        lock (sync)
        {
            selected = document ?? throw new InvalidOperationException("Load a playback document first.");
            if (position.CompareTo(selection.Start) < 0 || position.CompareTo(selection.End) >= 0) throw new ArgumentOutOfRangeException(nameof(position));
            resume = play ?? (state is MediaPlaybackState.Playing or MediaPlaybackState.Preparing);
        }
        long ticket = StopCurrent(MediaPlaybackState.Paused);
        await commands.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WaitForIdleAsync().ConfigureAwait(false);
            lock (sync) { if (ticket != generation) throw new OperationCanceledException("Superseded seek."); this.position = position; document = selected; if (audition.HasValue) original = audition.Value; }
            // Paused seeking prepares one exact displayed frame; the sink never opens.
            await BeginAsync(new(position, selection.End), ct, !resume).ConfigureAwait(false);
        }
        finally { commands.Release(); }
    }
    private async Task BeginAsync(TimeRange range, CancellationToken ct, bool paused = false)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            active?.Dispose();
            active = CancellationTokenSource.CreateLinkedTokenSource(ct);
            long current = ++generation;
            // Two queued frames plus the decoder's current/in-flight frame = at most three decoded pixel buffers.
            frames = Channel.CreateBounded<PreviewFrame>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = true });
            position = start = range.Start; end = range.End; Error = null; Waveform = []; state = MediaPlaybackState.Preparing;
            var loaded = document!; var audition = original; var queue = frames; var cancellation = active;
            running = Task.Run(() => RunAsync(loaded, range, audition, current, paused, queue, cancellation, ready));
        }
        Notify();
        await ready.Task.ConfigureAwait(false);
    }
    private async Task RunAsync(EditorDocument doc, TimeRange range, bool auditionOriginal, long current, bool paused,
        Channel<PreviewFrame> queue, CancellationTokenSource cancellation, TaskCompletionSource ready)
    {
        // Force all expensive preparation and cleanup off the caller's WPF dispatcher.
        await Task.Yield();
        IPreviewAudioSink? ownedSink = null;
        IPreparedMediaPlayback? prepared = null;
        Task producer = Task.CompletedTask;
        try
        {
            var ct = cancellation.Token;
            prepared = await preparation.PrepareAsync(doc, range, auditionOriginal, current, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            producer = ProduceAsync();
            async Task ProduceAsync()
            {
                try
                {
                    await prepared.StreamVideoAsync(async (frame, token) =>
                    {
                        if (frame.Generation != current || frame.ActualTime is null || frame.FrameIdentity is null)
                            throw new InvalidDataException("Streaming frame lost its generation/time/source identity.");
                        await queue.Writer.WriteAsync(frame, token).ConfigureAwait(false);
                        first.TrySetResult();
                        if (paused) await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    }, ct).ConfigureAwait(false);
                    queue.Writer.TryComplete(); first.TrySetResult();
                }
                catch (Exception ex) { queue.Writer.TryComplete(ex); first.TrySetException(ex); throw; }
            }
            // Do not start audio before at least one video frame is available.
            await first.Task.WaitAsync(ct).ConfigureAwait(false);
            if (!paused && prepared.Audio is { } pcm)
            {
                ownedSink = sinkFactory();
                await ownedSink.StartAsync(pcm, ct).ConfigureAwait(false);
            }
            lock (sync)
            {
                ct.ThrowIfCancellationRequested();
                if (current != generation) throw new OperationCanceledException(ct);
                sink = ownedSink; Waveform = prepared.Waveform; WaveformRange = prepared.Range;
                if (frameTimes.IsEmpty) frameTimes = prepared.FrameTimes;
                state = paused ? MediaPlaybackState.Paused : MediaPlaybackState.Playing;
                silentClock.Restart();
            }
            ready.TrySetResult(); Notify();
            if (paused)
            {
                // Keep only the first exact seek frame, cancel the decoder rather than filling a paused queue.
                cancellation.Cancel();
            }
            else
            {
                while (ownedSink is not null ? !ownedSink.Completion.IsCompleted : Position.CompareTo(range.End) < 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (producer.IsFaulted) await producer.ConfigureAwait(false);
                    if (ownedSink?.Completion.IsCompleted == true) { await ownedSink.Completion.ConfigureAwait(false); break; }
                    // Playback clock polling only while active, never a perpetual visual animation loop.
                    await Task.Delay(10, ct).ConfigureAwait(false);
                }
                if (ownedSink is not null) await ownedSink.Completion.ConfigureAwait(false);
                lock (sync) if (current == generation) { position = range.End; state = MediaPlaybackState.Stopped; }
                cancellation.Cancel();
            }
        }
        catch (OperationCanceledException) { ready.TrySetCanceled(cancellation.Token); }
        catch (Exception ex)
        {
            lock (sync) if (current == generation) { position = Position; Error = ex; state = MediaPlaybackState.Failed; }
            ready.TrySetException(ex);
        }
        finally
        {
            cancellation.Cancel();
            Exception? cleanupFailure = null;
            try { ownedSink?.Stop(); } catch (Exception ex) { cleanupFailure = ex; }
            try { await producer.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { cleanupFailure ??= ex; }
            try { if (ownedSink is not null) await ownedSink.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { cleanupFailure ??= ex; }
            try { if (prepared is not null) await prepared.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { cleanupFailure ??= ex; }
            lock (sync)
            {
                if (current == generation)
                {
                    if (state == MediaPlaybackState.Playing) position = Position;
                    if (cleanupFailure is not null && Error is null) { Error = cleanupFailure; state = MediaPlaybackState.Failed; }
                    sink = null; silentClock.Stop();
                    if (state is MediaPlaybackState.Playing or MediaPlaybackState.Preparing) state = MediaPlaybackState.Stopped;
                    if (!paused) while (queue.Reader.TryRead(out _)) { }
                }
            }
            Notify();
        }
    }
    /// <summary>Drop obsolete UI frames against the device clock; keep the next future frame queued.
    /// This method never advances the playback clock, including after a blocked dispatcher.</summary>
    public bool TryTakeFrame(out PreviewFrame? frame)
    {
        lock (sync)
        {
            frame = null;
            if (frames is null) return false;
            var now = Position;
            while (frames.Reader.TryPeek(out var next))
            {
                if (next.Generation != generation) { frames.Reader.TryRead(out _); continue; }
                if (next.ActualTime!.Value.CompareTo(now) > 0 && state != MediaPlaybackState.Paused) break;
                frames.Reader.TryRead(out frame);
                if (state == MediaPlaybackState.Paused) break;
            }
            return frame is not null;
        }
    }
    private long StopCurrent(MediaPlaybackState next)
    {
        IPreviewAudioSink? release;
        long ticket;
        lock (sync)
        {
            position = Position;
            ticket = ++generation; active?.Cancel(); release = sink; sink = null;
            silentClock.Stop(); state = next;
            if (frames is not null) while (frames.Reader.TryRead(out _)) { }
            frames = null;
        }
        try { release?.Stop(); }
        catch (Exception ex) { lock (sync) if (ticket == generation) { Error = ex; state = MediaPlaybackState.Failed; } }
        Notify();
        return ticket;
    }
    public Task WaitForIdleAsync() { lock (sync) return running; }
    private void Notify() => StateChanged?.Invoke(this, EventArgs.Empty);
    public async ValueTask DisposeAsync()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        Stop(); await commands.WaitAsync().ConfigureAwait(false);
        try { await WaitForIdleAsync().ConfigureAwait(false); active?.Dispose(); }
        finally { commands.Release(); }
    }
    private static RationalTime AddClock(RationalTime start, RationalTime elapsed)
    {
        // Preserve the exact start, quantize only elapsed device time (<1 microsecond), and avoid
        // overflowing RationalTime's denominator when adding two independently valid clock bases.
        int multiplier = Math.Max(1, 1_000_000 / start.Denominator);
        int scale = checked(start.Denominator * multiplier);
        BigInteger ticks = (BigInteger)start.Numerator * multiplier +
            (BigInteger)elapsed.Numerator * scale / elapsed.Denominator;
        return new(checked((long)ticks), scale);
    }
    private static RationalTime Minimum(RationalTime a, RationalTime b) => a.CompareTo(b) < 0 ? a : b;
}

/// <summary>Owns a fresh verified source session and bounded PCM artifact for each generation.</summary>
public sealed class MediaPlaybackPreparation(MediaProcess process, ISourceSnapshotService snapshots,
    string privateRoot, SizeI previewBounds) : IMediaPlaybackPreparation
{
    public async Task<IPreparedMediaPlayback> PrepareAsync(EditorDocument document, TimeRange range, bool original, long generation, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(privateRoot)) throw new ArgumentException("An absolute private preview root is required.");
        for (string? ancestor = Path.GetFullPath(privateRoot); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (new FileInfo(ancestor).LinkTarget is not null ||
                ((File.Exists(ancestor) || Directory.Exists(ancestor)) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Linked directories are not permitted in private preview staging.");
        string directory = Path.Combine(privateRoot, "playback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var probe = new MediaProbe(process, snapshots, Path.Combine(directory, "sources"));
        try
        {
            foreach (var source in document.Sources) await probe.InspectAsync(source, ct).ConfigureAwait(false);
            var capabilities = await process.DiscoverCapabilitiesAsync(ct).ConfigureAwait(false);
            var plan = await new MediaGraphCompiler(probe, capabilities).CompilePlaybackAsync(document, range, original, previewBounds, generation, ct).ConfigureAwait(false);
            AudioPcmInfo? pcm = null;
            ImmutableArray<WaveformBucket> waveform = [];
            if (plan.Audio is { } audio)
            {
                if (audio.Channels is > 2) throw new NotSupportedException("Native preview currently supports mono/stereo output; choose a supported channel layout.");
                pcm = await new MediaBinaryDecoder(process).DecodeAudioAsync(audio, Path.Combine(directory, "audition.pcm"), ct).ConfigureAwait(false);
                waveform = await new WaveformService().BuildAsync(pcm, 1024, ct).ConfigureAwait(false);
            }
            return new Prepared(probe, directory, new MediaPlaybackDecoder(process), plan, pcm, waveform);
        }
        catch { await probe.DisposeAsync().ConfigureAwait(false); Directory.Delete(directory, true); throw; }
    }
    private sealed class Prepared(MediaProbe probe, string directory, MediaPlaybackDecoder decoder,
        MediaPlaybackPlan plan, AudioPcmInfo? audio, ImmutableArray<WaveformBucket> waveform) : IPreparedMediaPlayback
    {
        public TimeRange Range => plan.Range;
        public AudioPcmInfo? Audio => audio;
        public ImmutableArray<WaveformBucket> Waveform => waveform;
        public bool HasVideo => plan.HasVideo;
        public ImmutableArray<RationalTime> FrameTimes => plan.FrameTimes;
        public Task StreamVideoAsync(Func<PreviewFrame, CancellationToken, ValueTask> consume, CancellationToken ct) => decoder.StreamVideoAsync(plan, consume, ct);
        public async ValueTask DisposeAsync() { await probe.DisposeAsync().ConfigureAwait(false); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
