using System.Collections.Immutable;
using System.Globalization;
using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Media;

/// <summary>Single-source crop adapter. Preview and export share captured identities and display-pixel coordinates.</summary>
public sealed class VideoCropEngine(MediaProcess process, MediaCapabilities capabilities, OutputWriter writer) : IConversionEngine, IEditorAdmissionInspector
{
    public const int MaximumOutputDimension = 8190;
    public static OperationDescriptor Operation { get; } = new("video.crop", "Crop", OperationMode.Tools, ".mp4", false, true);
    public IReadOnlyList<OperationDescriptor> Operations { get; } = [Operation];
    public EditorRouteRegistry Routes { get; } = new([new("video.crop", "Crop", EditorSubmissionShape.PerRoot)]);
    public ISourceSnapshotService Sources { get; } = new SourceSnapshotService();
    public MediaProcess Process => process;
    public MediaCapabilities Capabilities => capabilities;
    private static readonly IReadOnlyDictionary<string, string> Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { [".mp4"] = "mov", [".mov"] = "mov", [".m4v"] = "mov", [".mkv"] = "matroska", [".webm"] = "matroska", [".avi"] = "avi", [".wmv"] = "asf", [".gif"] = "gif" };
    public static bool Available(MediaCapabilities capabilities) =>
        new[] { "crop", "scale", "setsar", "transpose", "hflip", "vflip", "format" }.All(capabilities.Filters.Contains) &&
        new[] { "mpeg4", "aac", "rawvideo", "pcm_s16le" }.All(capabilities.Encoders.Contains) && capabilities.Muxers.Contains("mp4");

    public bool CanExecute(OperationRequest request)
    {
        if (request.Operation != Operation || request.Inputs.Count != 1 || !DropValidation.Accepts(request.Inputs, OperationMode.Tools) ||
            !Inputs.TryGetValue(Path.GetExtension(request.Inputs[0]), out var demuxer) || !capabilities.Demuxers.Contains(demuxer)) return false;
        if (request.Editor is null) return true; // Discovery only; execution requires the accepted frozen recipe.
        try { ValidateShape(request); return true; }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { return false; }
    }

    public async Task<MediaSourceInfo> InspectAsync(SourceSnapshot source, CancellationToken ct)
    {
        string directory = Path.Combine(Path.GetTempPath(), "citrus-crop-inspect-" + Guid.NewGuid().ToString("N"));
        await using var probe = new MediaProbe(process, Sources, directory);
        var info = await probe.InspectAsync(source, ct).ConfigureAwait(false);
        RequireVideo(info);
        return info;
    }

    public async Task<ImmutableArray<ValidationIssue>> ValidateAsync(OperationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var video = ValidateShape(request);
            var info = await InspectAsync(request.Editor!.Sources[0], ct).ConfigureAwait(false);
            ValidateAgainstSource(video, info);
            return [];
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        { return [new("Video crop", error.Message)]; }
    }

    public async Task<FrameStamp?> NextFrameAsync(SourceSnapshot source, MediaSourceInfo info, RationalTime after, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (source.Id != info.SourceId) throw new ArgumentException("Frame lookup requires the inspected source identity.");
        var video = RequireVideo(info);
        if (video.Frames.FirstOrDefault(frame => frame.SourceOffset.CompareTo(after) > 0) is { } cached) return cached;
        if (video.LastFrame is not { } last || last.SourceOffset.CompareTo(after) <= 0) return null;
        await using var probe = new MediaProbe(process, Sources, Path.Combine(Path.GetTempPath(), "citrus-crop-step-" + Guid.NewGuid().ToString("N")));
        await probe.InspectAsync(source, ct).ConfigureAwait(false);
        var frames = await probe.ReadFrameTimesAsync(source.Id, video.Index, new(after, info.Duration), 2, ct).ConfigureAwait(false);
        return frames.FirstOrDefault(frame => frame.SourceOffset.CompareTo(after) > 0);
    }

    public async Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CanExecute(request) || request.Editor is null) throw new NotSupportedException("Open the crop editor and apply a video selection first.");
        var video = ValidateShape(request);
        var target = EditorExportPolicy.Resolve(request);
        string scope = Path.Combine(Path.GetTempPath(), "citrus-crop-export-" + Guid.NewGuid().ToString("N"));
        await using var probe = new MediaProbe(process, Sources, scope);
        progress?.Report(new(null, "Verifying original video"));
        var source = request.Editor.Sources[0];
        var info = await probe.InspectAsync(source, ct).ConfigureAwait(false);
        ValidateAgainstSource(video, info);
        var stream = info.Video[0]; var crop = video.Crop!.Value;
        var filters = DisplayFilters(stream);
        filters.Add($"crop={Integer(crop.Width)}:{Integer(crop.Height)}:{Integer(crop.X)}:{Integer(crop.Y)}:exact=1");
        filters.Add("setsar=1");
        var quality = (QualityGoal)request.Editor.Export.Compression;
        string output = await writer.WriteFileAsync(Path.Combine(target.Directory, target.Stem + ".mp4"), async (temporary, token) =>
        {
            var arguments = new List<string> { "-noautorotate", "-display_rotation:v:0", "0" };
            arguments.AddRange(MediaProcess.InputArguments(probe.StagedSources[source.Id]));
            arguments.AddRange(["-map", $"0:{stream.Index}", "-map_metadata", "-1", "-map_chapters", "-1", "-vf", string.Join(',', filters),
                "-c:v", "mpeg4", "-q:v", Math.Max(2, 31 - quality.Value * .29).ToString("0.###", CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p"]);
            if (info.Audio.FirstOrDefault() is { } audio)
                arguments.AddRange(["-map", $"0:{audio.Index}", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", audio.Channels.Length.ToString(CultureInfo.InvariantCulture)]);
            else arguments.Add("-an");
            arguments.AddRange(["-threads", "2", "-f", "mp4", temporary]);
            await process.RunAsync(arguments, progress, token).ConfigureAwait(false);
            using var check = await process.ProbeJsonAsync(["-show_streams", "-of", "json", ..MediaProcess.InputArguments(temporary)], 2 * 1024 * 1024, token).ConfigureAwait(false);
            var streams = check.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var picture = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
            if (picture.GetProperty("width").GetInt32() != video.Canvas.Width || picture.GetProperty("height").GetInt32() != video.Canvas.Height ||
                !info.Audio.IsEmpty && !streams.Any(s => s.GetProperty("codec_type").GetString() == "audio" && s.GetProperty("channels").GetInt32() == info.Audio[0].Channels.Length))
                throw new InvalidDataException("The cropped copy did not retain its selected dimensions and audio stream.");
        }, ct).ConfigureAwait(false);
        progress?.Report(new(1, "Cropped copy ready"));
        return new([output]);
    }

    internal static List<string> DisplayFilters(VideoStreamInfo stream)
    {
        int width = checked((int)MediaTime.Ceiling(MediaTime.Mul(new(stream.CodedSize.Width, 1), stream.SampleAspect), 1));
        var filters = new List<string> { $"scale={width}:{stream.CodedSize.Height}", "setsar=1" };
        switch (stream.RotationDegrees)
        {
            case 90: filters.Add("transpose=cclock"); break;
            case 180: filters.AddRange(["hflip", "vflip"]); break;
            case 270: filters.Add("transpose=clock"); break;
            case 0: break;
            default: throw new NotSupportedException("Crop supports quarter-turn video rotation.");
        }
        return filters;
    }
    private static VideoRecipe ValidateShape(OperationRequest request)
    {
        if (request.Operation != Operation || request.Editor is not { Recipe: VideoRecipe video } doc ||
            !EditorRecipeValidator.Validate(doc).IsEmpty || doc.Sources.Length != 1 || !doc.Fonts.IsEmpty || video.Clips.Length != 1 ||
            video.Crop is not { } crop || video.Mute || !video.Masks.IsEmpty || !video.SplitAt.IsEmpty || video.SnapshotAt is not null || !video.MetadataEdits.IsEmpty ||
            video.Fit != ImageFit.Contain || doc.Export.Metadata != MetadataPolicy.StripAll || doc.Export.Compression is not QualityGoal { Value: >= 1 and <= 100 })
            throw new ArgumentException("Choose a supported single-video crop recipe.");
        var clip = video.Clips[0];
        if (crop.Width > MaximumOutputDimension || crop.Height > MaximumOutputDimension)
            throw new ArgumentException("This MP4 encoder supports crop dimensions up to 8190 pixels on each side.");
        if (clip.SourceId != doc.Sources[0].Id || clip.Speed != 1 || clip.Range.Start != new RationalTime(0, 1) ||
            new[] { crop.X, crop.Y, crop.Width, crop.Height }.Any(v => !double.IsFinite(v) || v != Math.Truncate(v)) ||
            crop.Width < 2 || crop.Height < 2 || crop.Width % 2 != 0 || crop.Height % 2 != 0 || video.Canvas != new SizeI((int)crop.Width, (int)crop.Height))
            throw new ArgumentException("Crop edges must be integral, with even output dimensions of at least two pixels.");
        _ = EditorExportPolicy.Resolve(request);
        return video;
    }
    private static VideoStreamInfo RequireVideo(MediaSourceInfo info)
    {
        var stream = info.Video.FirstOrDefault() ?? throw new NotSupportedException("The selected file contains no video stream.");
        if (stream.IsHdr) throw new NotSupportedException("HDR cropping requires a validated color-preservation pipeline. Choose an SDR copy.");
        if (stream.DisplaySize.Width < 2 || stream.DisplaySize.Height < 2) throw new NotSupportedException("Video dimensions must be at least two pixels.");
        return stream;
    }
    private static void ValidateAgainstSource(VideoRecipe video, MediaSourceInfo info)
    {
        var stream = RequireVideo(info); var crop = video.Crop!.Value; var clip = video.Clips[0];
        if (clip.VideoStream != stream.Index || clip.AudioStream != info.Audio.FirstOrDefault()?.Index || clip.Range.End != info.Duration ||
            crop.X + crop.Width > stream.DisplaySize.Width || crop.Y + crop.Height > stream.DisplaySize.Height)
            throw new ArgumentException("The crop must fit the inspected video, with its original duration and streams.");
    }
    private static string Integer(double value) => checked((int)value).ToString(CultureInfo.InvariantCulture);
}
