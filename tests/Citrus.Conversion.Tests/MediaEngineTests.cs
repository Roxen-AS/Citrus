using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Citrus.Core;

namespace Citrus.Conversion.Tests;

// These are host engine integration tests. Windows shipped-binary smoke tests are separate.
public sealed class MediaEngineTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Citrus media ü " + Guid.NewGuid().ToString("N"));
    private MediaProcess process = null!;
    private MediaEngine engine = null!;
    private string tone = "";
    private string video = "";
    private static string Ffmpeg => Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG") ?? "/usr/bin/ffmpeg";
    private static string Ffprobe => Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE") ?? "/usr/bin/ffprobe";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        tone = Path.Combine(directory, "-tone ' café.wav");
        video = Path.Combine(directory, "-pattern ' 日本語.mp4");
        await Native(Ffmpeg, "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=4", "-c:a", "pcm_s16le", tone);
        await Native(Ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=24:duration=4", "-i", tone, "-c:v", "mpeg4", "-c:a", "aac", "-shortest", video);
        process = new(Ffmpeg, Ffprobe);
        engine = await MediaEngine.CreateAsync(process, CancellationToken.None);
    }
    public Task DisposeAsync() { Directory.Delete(directory, true); return Task.CompletedTask; }

    [Theory]
    [InlineData("mp3", "mp3")][InlineData("m4a", "aac")][InlineData("wav", "pcm_s16le")][InlineData("flac", "flac")]
    [InlineData("ogg", "vorbis")][InlineData("opus", "opus")][InlineData("aiff", "pcm_s16be")][InlineData("wma", "wmav2")]
    public async Task Audio_formats_are_real_and_preserve_original(string format, string codec)
    {
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(tone));
        string output = await Execute("media.convert." + format, tone);
        Assert.NotEqual(tone, output);
        using var info = await Probe(output);
        Assert.Equal(codec, Stream(info, "audio").GetProperty("codec_name").GetString());
        Assert.InRange(Duration(info), 3.8, 4.3);
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(tone)));
    }

    [Theory]
    [InlineData("mp4", "mpeg4")][InlineData("mov", "mpeg4")][InlineData("mkv", "mpeg4")]
    [InlineData("webm", "vp9")][InlineData("avi", "mpeg4")][InlineData("wmv", "wmv2")][InlineData("gif", "gif")]
    public async Task Video_formats_encode_real_frames(string format, string codec)
    {
        using var info = await Probe(await Execute("media.convert." + format, video));
        Assert.Equal(codec, Stream(info, "video").GetProperty("codec_name").GetString());
        Assert.InRange(Duration(info), 3.8, 4.3);
        Assert.Equal(160, Stream(info, "video").GetProperty("width").GetInt32());
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Trim_encodes_requested_interval(bool audio)
    {
        using var info = await Probe(await Execute(audio ? "audio.trim" : "video.trim", audio ? tone : video, new() { StartSeconds = 1, EndSeconds = 2.5 }));
        Assert.InRange(Duration(info), 1.4, 1.65);
    }

    [Fact]
    public async Task Resize_preserves_ratio()
    {
        using var info = await Probe(await Execute("video.resize", video, new() { Width = 80 }));
        var stream = Stream(info, "video");
        Assert.Equal(80, stream.GetProperty("width").GetInt32());
        Assert.Equal(60, stream.GetProperty("height").GetInt32());
    }

    [Theory][InlineData(0.25)][InlineData(4)]
    public async Task Speed_boundaries_keep_audio_synchronized(double speed)
    {
        using var info = await Probe(await Execute("video.speed", video, new() { Speed = speed }));
        Assert.InRange(Duration(info), 4 / speed - .2, 4 / speed + .3);
        double audio = double.Parse(Stream(info, "audio").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        double picture = double.Parse(Stream(info, "video").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(Math.Abs(audio - picture), 0, .2);
    }

    [Fact]
    public async Task Normalize_reaches_default_minus16_lufs()
    {
        string output = await Execute("audio.normalize", tone);
        string measurement = await Native(Ffmpeg, "-hide_banner", "-i", output, "-af", "loudnorm=I=-16:print_format=json", "-f", "null", "-");
        using var json = JsonDocument.Parse(measurement[measurement.LastIndexOf('{')..(measurement.LastIndexOf('}') + 1)]);
        double loudness = double.Parse(json.RootElement.GetProperty("input_i").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(loudness, -16.5, -15.5);
    }

    [Fact]
    public async Task Quoted_unicode_leading_dash_paths_are_passed_as_arguments()
    {
        string path = Path.Combine(directory, OperatingSystem.IsWindows() ? "-other ' ü.wav" : "-other \" ' ü.wav");
        File.Copy(tone, path);
        using var info = await Probe(await Execute("media.convert.flac", path));
        Assert.Equal("flac", Stream(info, "audio").GetProperty("codec_name").GetString());
    }

    [Fact]
    public async Task Cancellation_terminates_live_process_and_cleans_staged_output()
    {
        using var cancel = new CancellationTokenSource();
        string desired = Path.Combine(directory, "canceled.wav");
        var writer = new OutputWriter();
        var progress = new CallbackProgress(_ => cancel.Cancel());
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteFileAsync(desired,
            (temporary, token) => process.RunAsync(["-re", "-i", tone, "-c:a", "pcm_s16le", temporary], progress, token), cancel.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.False(File.Exists(desired));
        Assert.Empty(Directory.GetFiles(directory, "*.citrus-*"));
    }

    [Fact]
    public async Task Animated_gif_retains_multiple_frames_as_video_input()
    {
        string gif = await Execute("media.convert.gif", video);
        using var info = await Probe(await Execute("media.convert.mp4", gif));
        Assert.True(int.Parse(Stream(info, "video").GetProperty("nb_frames").GetString()!) > 2);
        Assert.InRange(Duration(info), 3.8, 4.3);
    }

    [Fact]
    public async Task Missing_binaries_fail_clearly_without_download()
    {
        var missing = new MediaProcess(Path.Combine(directory, "missing-ffmpeg.exe"), Path.Combine(directory, "missing-ffprobe.exe"));
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => MediaEngine.CreateAsync(missing, CancellationToken.None));
        Assert.Contains("reinstall", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_options_leave_no_outputs()
    {
        foreach (var options in new[] { new OperationOptions { Speed = .24 }, new OperationOptions { Speed = 4.01 }, new OperationOptions { Speed = double.NaN } })
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Execute("video.speed", video, options));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Execute("audio.trim", tone, new() { StartSeconds = 1, EndSeconds = 5 }));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public async Task Runtime_protocol_whitelist_rejects_network_even_on_host_binary()
    {
        await Assert.ThrowsAsync<MediaProcessException>(() => process.RunAsync(["-i", "https://127.0.0.1:1/no.mp4", "-f", "null", "-"], null, CancellationToken.None));
    }

    [Fact]
    public async Task Progress_contains_machine_readable_time_and_completes()
    {
        var reports = new List<ConversionProgress>();
        await Execute("media.convert.flac", tone, progress: new CallbackProgress(reports.Add));
        Assert.Contains(reports, p => p.Message.Contains("Encoding", StringComparison.Ordinal));
        Assert.Equal(1, reports[^1].Fraction);
    }

    [Fact]
    public async Task Canceling_engine_during_encoding_never_commits_an_output()
    {
        using var cancel = new CancellationTokenSource();
        var operation = Assert.Single(engine.Operations, op => op.Id == "media.convert.flac");
        var progress = new CallbackProgress(p => { if (p.Message.StartsWith("Encoding", StringComparison.Ordinal)) cancel.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExecuteAsync(new(operation, [tone], new(), null), progress, cancel.Token));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public void Capabilities_come_from_host_binary_without_assuming_software_h264()
    {
        Assert.Contains("mpeg4", engine.Capabilities.Encoders);
        Assert.Contains("mp4", engine.Capabilities.Muxers);
        if (!OperatingSystem.IsWindows()) Assert.DoesNotContain(engine.Operations, o => o.Id == "media.convert.mp4.h264");
        Assert.DoesNotContain(engine.Operations, o => o.Id.Contains("x264", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_kills_a_process_tree_and_stderr_is_bounded()
    {
        if (OperatingSystem.IsWindows()) return; // Unix process fixture; Windows cancellation also has the real FFmpeg tests.
        string script = Path.Combine(directory, "process-tree.sh");
        string pidFile = Path.Combine(directory, "child.pid");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 60 &\necho $! > '" + pidFile + "'\nwait\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var child = new MediaProcess(script, Ffprobe);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => child.RunAsync([], null, cancel.Token));
        int pid = int.Parse(await File.ReadAllTextAsync(pidFile));
        string procStat = "/proc/" + pid + "/stat";
        Assert.True(!File.Exists(procStat) || (await File.ReadAllTextAsync(procStat)).Contains(") Z ", StringComparison.Ordinal));
        await File.WriteAllTextAsync(script, "#!/bin/sh\nhead -c 100000 /dev/zero | tr '\\0' x >&2\nexit 9\n");
        var error = await Assert.ThrowsAsync<MediaProcessException>(() => child.RunAsync([], null, CancellationToken.None));
        Assert.InRange(error.Message.Length, 32000, 33000);
    }

    private async Task<string> Execute(string id, string input, OperationOptions? options = null, IProgress<ConversionProgress>? progress = null)
    {
        var operation = Assert.Single(engine.Operations, op => op.Id == id);
        return Assert.Single((await engine.ExecuteAsync(new(operation, [input], options ?? new(), null), progress, CancellationToken.None)).Outputs);
    }
    private static async Task<JsonDocument> Probe(string path) => JsonDocument.Parse(await Native(Ffprobe, "-v", "error", "-show_streams", "-show_format", "-of", "json", path));
    private static JsonElement Stream(JsonDocument info, string kind) => info.RootElement.GetProperty("streams").EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == kind);
    private static double Duration(JsonDocument info) => double.Parse(info.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
    private static async Task<string> Native(string exe, params string[] arguments)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync();
        string result = await stdout; string error = await stderr;
        Assert.True(child.ExitCode == 0, error);
        return result.Length > 0 ? result : error;
    }
    private sealed class CallbackProgress(Action<ConversionProgress> action) : IProgress<ConversionProgress> { public void Report(ConversionProgress value) => action(value); }
}
