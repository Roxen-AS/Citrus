using System.Collections.Immutable;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Media;

/// <summary>Prepared exact stamps are bounded metadata; decoded pixels are streamed separately.</summary>
public sealed class MediaPlaybackPlan
{
    internal MediaPlaybackPlan(TimeRange range, ImmutableArray<VideoPlaybackSegment> video, MediaGraphPlan? audio)
    { Range = range; Video = video; Audio = audio; }
    public TimeRange Range { get; }
    public MediaGraphPlan? Audio { get; }
    public bool HasVideo => !Video.IsEmpty;
    public ImmutableArray<RationalTime> FrameTimes => Video.SelectMany(s => s.Frames.Select(f => f.Time)).ToImmutableArray();
    internal ImmutableArray<VideoPlaybackSegment> Video { get; }
}
internal sealed record VideoPlaybackSegment(MediaGraphPlan Graph, ImmutableArray<PlaybackStamp> Frames);
internal sealed record PlaybackStamp(SourceFrameRef Identity, RationalTime Time);

public sealed partial class MediaGraphCompiler
{
    public const int MaximumPlaybackFrames = 32768;
    public const int MaximumPlaybackClips = 64;
    public const int MaximumPlaybackSeconds = 120;

    /// <summary>Original bypasses pixel/sample effects, retaining the same trim/order/speed and format.
    /// Full timestamp scans are visible preparation work. Over-limit auditions fail, never truncate.</summary>
    public async Task<MediaPlaybackPlan> CompilePlaybackAsync(EditorDocument document, TimeRange range,
        bool original, SizeI bounds, long generation, CancellationToken ct)
    {
        var issues = EditorRecipeValidator.Validate(document);
        if (!issues.IsEmpty) throw new ArgumentException(string.Join("; ", issues.Select(i => i.Message)));
        var clips = document.Recipe switch { VideoRecipe v => v.Clips, AudioRecipe a => a.Clips,
            _ => throw new NotSupportedException("Playback supports video and audio recipes.") };
        if (clips.Length > MaximumPlaybackClips) throw new NotSupportedException("Preview supports at most 64 clips.");
        var duration = clips.Aggregate(MediaTime.Zero, (time, clip) => MediaTime.Add(time,
            MediaTime.Div(MediaTime.Sub(clip.Range.End, clip.Range.Start), MediaTime.Speed(clip.Speed))));
        if (range.Start.CompareTo(MediaTime.Zero) < 0 || range.End.CompareTo(range.Start) <= 0 || range.End.CompareTo(duration) > 0)
            throw new ArgumentOutOfRangeException(nameof(range));
        if (MediaTime.Sub(range.End, range.Start).CompareTo(new(MaximumPlaybackSeconds, 1)) > 0)
            throw new NotSupportedException("Select an audition range of at most two minutes.");
        // Slice in exact assembled time, then map to source time without subtracting the origin twice.
        var selected = ImmutableArray.CreateBuilder<MediaClip>();
        var cursor = MediaTime.Zero;
        foreach (var clip in clips)
        {
            var end = MediaTime.Add(cursor, MediaTime.Div(MediaTime.Sub(clip.Range.End, clip.Range.Start), MediaTime.Speed(clip.Speed)));
            var a = Max(cursor, range.Start); var b = Min(end, range.End);
            if (a.CompareTo(b) < 0) selected.Add(clip with { Range = new(
                MediaTime.Add(clip.Range.Start, MediaTime.Mul(MediaTime.Sub(a, cursor), MediaTime.Speed(clip.Speed))),
                MediaTime.Add(clip.Range.Start, MediaTime.Mul(MediaTime.Sub(b, cursor), MediaTime.Speed(clip.Speed)))) });
            cursor = end;
        }
        var sliced = selected.ToImmutable();
        EditorRecipe recipe = document.Recipe switch
        {
            VideoRecipe v => v with { Clips = sliced, SnapshotAt = null },
            AudioRecipe a => a with { Clips = sliced }, _ => throw new NotSupportedException()
        };
        // Effects use assembled coordinates. Until their renderer exists, never rebase them silently.
        if (original) recipe = recipe switch
        {
            VideoRecipe v => v with { Crop = null, Mute = false, Masks = [], SplitAt = [], MetadataEdits = [] },
            AudioRecipe a => a with { Bleeps = [], Loudness = null, MetadataEdits = [] }, _ => recipe
        };
        var slicedDocument = document with { Recipe = recipe };
        var request = new PreviewRequest(generation, bounds, MediaTime.Zero, null, null);
        MediaGraphPlan? audio = null;
        if (recipe is AudioRecipe) audio = Compile(slicedDocument, RenderPurpose.Preview, request, probe.StagedSources);
        if (recipe is VideoRecipe video && !video.Mute && sliced.Any(c => c.AudioStream.HasValue))
        {
            if (sliced.Any(c => c.AudioStream is null))
                throw new NotSupportedException("Mixed audible/silent clips require the video editor's silence-padding graph.");
            var first = probe.GetInfo(sliced[0].SourceId).Audio.Single(s => s.Index == sliced[0].AudioStream);
            var names = first.Channels;
            var matrix = new ChannelMatrix(names, names, Enumerable.Range(0, names.Length * names.Length)
                .Select(i => i / names.Length == i % names.Length ? 1d : 0d).ToImmutableArray(), false);
            audio = Compile(slicedDocument with { Recipe = new AudioRecipe(sliced, first.SampleRate, matrix, [], null, []) },
                RenderPurpose.Preview, request, probe.StagedSources);
        }
        var segments = ImmutableArray.CreateBuilder<VideoPlaybackSegment>();
        var originalStarts = new Dictionary<Guid, RationalTime>();
        cursor = MediaTime.Zero;
        foreach (var clip in clips)
        {
            originalStarts.Add(clip.Id, cursor);
            cursor = MediaTime.Add(cursor, MediaTime.Div(MediaTime.Sub(clip.Range.End, clip.Range.Start), MediaTime.Speed(clip.Speed)));
        }
        // Compile visual transforms against the original timeline/first display plane. Slicing a
        // playback range must not change the crop coordinate plane or discard the seek predecessor.
        var videoDocument = recipe is VideoRecipe visual ? document with { Recipe = visual with { Clips = clips } } : document;
        int frameCount = 0;
        if (recipe is VideoRecipe)
        foreach (var clip in sliced)
        {
            ct.ThrowIfCancellationRequested();
            int stream = clip.VideoStream ?? throw new NotSupportedException("Every video clip needs a video stream.");
            var stamps = ImmutableArray.CreateBuilder<FrameStamp>();
            var originalClip = clips.Single(c => c.Id == clip.Id);
            var next = originalClip.Range.Start;
            FrameStamp? predecessor = null;
            while (next.CompareTo(clip.Range.End) < 0)
            {
                var page = await probe.ReadFrameTimesAsync(clip.SourceId, stream, new(next, clip.Range.End), 4096, ct).ConfigureAwait(false);
                foreach (var stamp in page)
                {
                    if (stamps.Count > 0 && stamp.RawPts == stamps[^1].RawPts) continue;
                    if (stamp.SourceOffset.CompareTo(clip.Range.Start) < 0) { predecessor = stamp; continue; }
                    if (stamps.Count == 0 && predecessor is not null && stamp.SourceOffset.CompareTo(clip.Range.Start) > 0) AddStamp(predecessor);
                    AddStamp(stamp);
                }
                if (page.Length < 4096) break;
                // Include the predecessor in the next registry window; no FPS-derived step.
                if (page[^1].SourceOffset.CompareTo(next) <= 0) throw new MediaProcessException("Frame pagination did not advance.");
                next = page[^1].SourceOffset;
            }
            if (stamps.Count == 0 && predecessor is not null) AddStamp(predecessor);
            if (stamps.Count == 0) throw new NotSupportedException("The selected video interval has no actual frame.");
            void AddStamp(FrameStamp stamp)
            {
                if (++frameCount > MaximumPlaybackFrames) throw new NotSupportedException("Audition exceeds 32768 actual frames; select a shorter interval.");
                stamps.Add(stamp);
            }
            // Re-prepare the first window if pagination evicted it. Geometry still comes solely from Video().
            await probe.ReadFrameTimesAsync(clip.SourceId, stream, new(stamps[0].SourceOffset, clip.Range.End), 4096, ct).ConfigureAwait(false);
            var firstTime = MediaTime.Add(originalStarts[clip.Id], MediaTime.Div(MediaTime.Sub(stamps[0].SourceOffset, originalClip.Range.Start), MediaTime.Speed(clip.Speed)));
            var graph = Compile(videoDocument, RenderPurpose.Preview, request with { Time = firstTime }, probe.StagedSources);
            var arguments = graph.Arguments.ToArray();
            int filter = Array.IndexOf(arguments, "-vf") + 1;
            var exactTrim = $"trim=start_pts={Number(stamps[0].RawPts)}:end_pts={Number(checked(stamps[0].RawPts + 1))}";
            var streamTrim = $"trim=start_pts={Number(stamps[0].RawPts)}:end_pts={Number(checked(stamps[^1].RawPts + 1))}";
            if (!arguments[filter].Contains(exactTrim, StringComparison.Ordinal)) throw new InvalidOperationException("Exact frame graph lost its trim identity.");
            arguments[filter] = arguments[filter].Replace(exactTrim, streamTrim, StringComparison.Ordinal);
            arguments[Array.IndexOf(arguments, "-frames:v") + 1] = Number(stamps.Count);
            var streamingGraph = new MediaGraphPlan(graph.Revision, graph.Generation, graph.FrameTime, graph.FrameIdentity,
                [..arguments], graph.VideoSize, null, null, graph.Duration, graph.Sources, graph.Owner);
            segments.Add(new(streamingGraph, stamps.Select(s => new PlaybackStamp(new(clip.SourceId, stream, s.RawPts),
                MediaTime.Add(originalStarts[clip.Id], MediaTime.Div(MediaTime.Sub(s.SourceOffset, originalClip.Range.Start), MediaTime.Speed(clip.Speed))))).ToImmutableArray()));
        }
        return new(range, segments.ToImmutable(), audio);
    }
    private static RationalTime Min(RationalTime a, RationalTime b) => a.CompareTo(b) < 0 ? a : b;
    private static RationalTime Max(RationalTime a, RationalTime b) => a.CompareTo(b) > 0 ? a : b;
}
