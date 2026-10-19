using System.Globalization;
using Citrus.Core;

namespace Citrus.Conversion;

/// <summary>Capability-selected offline presets. Video tools produce MP4; audio tools produce lossless WAV copies.</summary>
public sealed class MediaEngine : IConversionEngine
{
    private sealed record Preset(string Extension, string Muxer, string? VideoEncoder, string? AudioEncoder);
    private static readonly Preset[] Presets =
    [
        new("mp4", "mp4", "mpeg4", "aac"), new("mov", "mov", "mpeg4", "aac"), new("mkv", "matroska", "mpeg4", "aac"),
        new("webm", "webm", "libvpx-vp9", "libopus"), new("avi", "avi", "mpeg4", "libmp3lame"), new("wmv", "asf", "wmv2", "wmav2"), new("gif", "gif", "gif", null),
        new("mp3", "mp3", null, "libmp3lame"), new("m4a", "ipod", null, "aac"), new("wav", "wav", null, "pcm_s16le"),
        new("flac", "flac", null, "flac"), new("ogg", "ogg", null, "libvorbis"), new("opus", "opus", null, "libopus"),
        new("aiff", "aiff", null, "pcm_s16be"), new("wma", "asf", null, "wmav2")
    ];
    private static readonly IReadOnlyDictionary<string, string> AudioInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { [".mp3"] = "mp3", [".m4a"] = "mov", [".wav"] = "wav", [".flac"] = "flac", [".ogg"] = "ogg", [".opus"] = "ogg", [".aiff"] = "aiff", [".aif"] = "aiff", [".wma"] = "asf", [".aac"] = "aac" };
    private static readonly IReadOnlyDictionary<string, string> VideoInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { [".mp4"] = "mov", [".mov"] = "mov", [".mkv"] = "matroska", [".webm"] = "matroska", [".avi"] = "avi", [".wmv"] = "asf", [".gif"] = "gif", [".m4v"] = "mov" };
    private readonly MediaProcess process;
    private readonly MediaCapabilities capabilities;
    private readonly Dictionary<string, Preset> routes = new(StringComparer.Ordinal);
    private readonly OutputWriter writer = new();
    public IReadOnlyList<OperationDescriptor> Operations { get; }
    public MediaCapabilities Capabilities => capabilities;
    private MediaEngine(MediaProcess process, MediaCapabilities capabilities)
    {
        this.process = process; this.capabilities = capabilities;
        var operations = new List<OperationDescriptor>();
        foreach (var preset in Presets.Where(Supported))
        {
            string id = "media.convert." + preset.Extension;
            routes[id] = preset;
            operations.Add(new(id, preset.Extension.ToUpperInvariant(), OperationMode.Convert, "." + preset.Extension, false, false));
        }
        if (capabilities.MediaFoundationH264 && Supported(new("mp4", "mp4", "h264_mf", "aac")))
        {
            routes["media.convert.mp4.h264"] = new("mp4", "mp4", "h264_mf", "aac");
            operations.Add(new("media.convert.mp4.h264", "MP4 (H.264)", OperationMode.Convert, ".mp4", false, false));
        }
        AddTool("audio.trim", "Trim audio", "wav");
        if (capabilities.Filters.Contains("loudnorm")) AddTool("audio.normalize", "Normalize audio", "wav");
        AddTool("video.trim", "Trim video", "mp4");
        if (capabilities.Filters.Contains("scale")) AddTool("video.resize", "Resize video", "mp4");
        if (capabilities.Filters.Contains("setpts") && capabilities.Filters.Contains("atempo")) AddTool("video.speed", "Change speed", "mp4");
        if (routes.TryGetValue("media.convert.mp3", out var mp3))
        {
            routes["video.audio"] = mp3;
            operations.Add(new("video.audio", "Extract MP3 audio", OperationMode.Tools, ".mp3", false, false));
        }
        Operations = operations.AsReadOnly();
        void AddTool(string id, string label, string extension)
        {
            if (!routes.TryGetValue("media.convert." + extension, out var preset)) return;
            routes[id] = preset;
            operations.Add(new(id, label, OperationMode.Tools, "." + extension, false, true));
        }
    }
    public static async Task<MediaEngine> CreateAsync(MediaProcess process, CancellationToken cancellationToken = default) =>
        new(process, await process.DiscoverCapabilitiesAsync(cancellationToken).ConfigureAwait(false));
    private bool Supported(Preset preset) => capabilities.Muxers.Contains(preset.Muxer) &&
        (preset.VideoEncoder is null || capabilities.Encoders.Contains(preset.VideoEncoder)) &&
        (preset.AudioEncoder is null || capabilities.Encoders.Contains(preset.AudioEncoder));

    public bool CanExecute(OperationRequest request)
    {
        if (request.Inputs.Count != 1 || !routes.TryGetValue(request.Operation.Id, out var preset) || !DropValidation.Accepts(request.Inputs, request.Operation.Mode)) return false;
        string extension = Path.GetExtension(request.Inputs[0]);
        bool video = VideoInputs.TryGetValue(extension, out string? demuxer);
        if (!video && !AudioInputs.TryGetValue(extension, out demuxer)) return false;
        if (!capabilities.Demuxers.Contains(demuxer!)) return false;
        if (request.Operation.Id.StartsWith("audio.", StringComparison.Ordinal)) return !video;
        if (request.Operation.Id.StartsWith("video.", StringComparison.Ordinal) && !video) return false;
        if (preset.VideoEncoder is not null) return video;
        return !video || (!extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) && (request.Operation.Id is "media.convert.mp3" or "video.audio"));
    }

    public async Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanExecute(request)) throw new NotSupportedException("Choose one local audio or video file and an available media operation.");
        var options = request.Options;
        if (options.Quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request), "Quality must be between 1 and 100.");
        string id = request.Operation.Id; var preset = routes[id];
        if (id == "video.speed" && (!double.IsFinite(options.Speed) || options.Speed is < .25 or > 4)) throw new ArgumentOutOfRangeException(nameof(request), "Speed must be between 0.25 and 4 times.");
        if (id == "video.resize" && (options.Width is <= 0 || options.Height is <= 0 || (options.Width is null && options.Height is null))) throw new ArgumentOutOfRangeException(nameof(request), "Resize requires positive dimensions.");
        if (id == "audio.normalize" && (!double.IsFinite(options.NormalizeLufs) || options.NormalizeLufs is < -70 or > -5)) throw new ArgumentOutOfRangeException(nameof(request), "Normalization must be between -70 and -5 LUFS.");
        string source = Path.GetFullPath(request.Inputs[0]);
        progress?.Report(new(null, "Reading media"));
        var info = await process.ProbeAsync(source, cancellationToken).ConfigureAwait(false);
        if (preset.VideoEncoder is not null && !info.HasVideo || preset.VideoEncoder is null && !info.HasAudio) throw new NotSupportedException("The file does not contain the required media stream.");
        bool trim = id is "audio.trim" or "video.trim";
        if (trim && (!double.IsFinite(options.StartSeconds) || options.StartSeconds < 0 || info.DurationSeconds <= 0 ||
            options.StartSeconds >= info.DurationSeconds || options.EndSeconds is double end && (!double.IsFinite(end) || end <= options.StartSeconds || end > info.DurationSeconds)))
            throw new ArgumentOutOfRangeException(nameof(request), "Trim start and end must be within the media duration, with end later than start.");
        string suffix = request.Operation.Mode == OperationMode.Tools ? "-" + id[(id.IndexOf('.') + 1)..] : "";
        string desired = Path.Combine(request.DestinationDirectory ?? Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + suffix + "." + preset.Extension);
        string output = await writer.WriteFileAsync(desired, async (temporary, token) =>
        {
            var args = new List<string> { "-i", source };
            if (trim) { args.AddRange(["-ss", Number(options.StartSeconds)]); if (options.EndSeconds is double end) args.AddRange(["-t", Number(end - options.StartSeconds)]); }
            args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1"]);
            if (preset.VideoEncoder is null) args.AddRange(["-map", "0:a:0", "-vn"]);
            else
            {
                args.AddRange(["-map", "0:v:0", "-c:v", preset.VideoEncoder]);
                if (preset.AudioEncoder is not null) args.AddRange(["-map", "0:a:0?"]);
                else args.Add("-an");
                if (preset.VideoEncoder != "gif") args.AddRange(["-pix_fmt", "yuv420p"]);
                if (preset.VideoEncoder == "h264_mf") args.AddRange(["-hw_encoding", "0", "-b:v", "4M"]);
                else if (preset.VideoEncoder == "libvpx-vp9") args.AddRange(["-crf", Number(63 - options.Quality * .6), "-b:v", "0", "-deadline", "good", "-cpu-used", "4"]);
                else if (preset.VideoEncoder is "mpeg4" or "wmv2") args.AddRange(["-q:v", Number(Math.Max(2, 31 - options.Quality * .29))]);
                var videoFilters = new List<string>();
                if (id == "video.resize")
                {
                    string w = options.Width?.ToString(CultureInfo.InvariantCulture) ?? "-2";
                    string h = options.Height?.ToString(CultureInfo.InvariantCulture) ?? "-2";
                    string fit = options.PreserveAspectRatio && options.Width is not null && options.Height is not null ? ":force_original_aspect_ratio=decrease:force_divisible_by=2" : "";
                    videoFilters.Add($"scale={w}:{h}{fit}");
                }
                else if (preset.VideoEncoder != "gif") videoFilters.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
                if (id == "video.speed") videoFilters.Add($"setpts=(PTS-STARTPTS)/{Number(options.Speed)}");
                if (videoFilters.Count != 0) args.AddRange(["-vf", string.Join(',', videoFilters)]);
            }
            if (preset.AudioEncoder is not null)
            {
                args.AddRange(["-c:a", preset.AudioEncoder]);
                if (preset.AudioEncoder is "aac" or "libmp3lame" or "libopus" or "libvorbis" or "wmav2") args.AddRange(["-b:a", "192k", "-ar", "48000", "-ac", "2"]);
                if (id == "audio.normalize") args.AddRange(["-af", $"loudnorm=I={Number(options.NormalizeLufs)}:TP=-1.5:LRA=11", "-ar", "48000"]);
                if (id == "video.speed" && info.HasAudio) args.AddRange(["-af", Tempo(options.Speed)]);
            }
            args.AddRange(["-threads", "2", "-f", preset.Muxer, temporary]);
            await process.RunAsync(args, progress, token).ConfigureAwait(false);
            var check = await process.ProbeAsync(temporary, token).ConfigureAwait(false);
            if (preset.VideoEncoder is null ? !check.HasAudio : !check.HasVideo) throw new IOException("The media engine did not produce the expected stream.");
        }, cancellationToken).ConfigureAwait(false);
        progress?.Report(new(1, "Copy ready"));
        return new([output]);
    }
    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static string Tempo(double speed)
    {
        var filters = new List<string>();
        while (speed < .5) { filters.Add("atempo=0.5"); speed /= .5; }
        while (speed > 2) { filters.Add("atempo=2"); speed /= 2; }
        filters.Add("atempo=" + Number(speed));
        return string.Join(',', filters);
    }
}
