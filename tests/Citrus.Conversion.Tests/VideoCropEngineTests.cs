using System.Security.Cryptography;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Media;
using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Tests;

public sealed class VideoCropEngineTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "citrus-video-crop-" + Guid.NewGuid().ToString("N"));
    private MediaProcess process = null!;
    private EngineRegistry registry = null!;
    private string input = "";
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        string ffmpeg = Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG") ?? "/usr/bin/ffmpeg";
        string ffprobe = Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE") ?? "/usr/bin/ffprobe";
        process = new(ffmpeg, ffprobe);
        string raw = Path.Combine(root, "pattern.bgra"); input = Path.Combine(root, "portrait ' 日本語.mp4");
        using (var frames = File.Create(raw))
            for (int frame = 0; frame < 12; frame++)
                for (int y = 0; y < 120; y++) for (int x = 0; x < 96; x++)
                {
                    byte shade = (byte)(x < 48 ? (y < 60 ? 30 : 90) : (y < 60 ? 160 : 220));
                    frames.Write([shade, shade, shade, 255]);
                }
        await process.RunAsync(["-f", "rawvideo", "-pixel_format", "bgra", "-video_size", "96x120", "-framerate", "12", "-i", raw, "-c:v", "mpeg4", "-q:v", "1", "-pix_fmt", "yuv420p", input], null, default);
        registry = await EngineRegistry.Create(new(), Path.GetDirectoryName(Path.GetFullPath(ffmpeg))!);
    }
    public Task DisposeAsync() { registry.ImageEditors?.Dispose(); Directory.Delete(root, true); return Task.CompletedTask; }

    [Fact]
    public async Task Surround_crop_retains_six_audio_channels()
    {
        var engine = registry.VideoCrop; Assert.NotNull(engine);
        string wave = Path.Combine(root, "surround.wav"), muxed = Path.Combine(root, "surround.mp4");
        using (var wav = new BinaryWriter(File.Create(wave)))
        {
            const int bytes = 48000 * 6 * 2;
            wav.Write("RIFF"u8); wav.Write(36 + bytes); wav.Write("WAVEfmt "u8); wav.Write(16); wav.Write((short)1); wav.Write((short)6);
            wav.Write(48000); wav.Write(48000 * 12); wav.Write((short)12); wav.Write((short)16); wav.Write("data"u8); wav.Write(bytes); wav.Write(new byte[bytes]);
        }
        await process.RunAsync([..MediaProcess.InputArguments(input), ..MediaProcess.InputArguments(wave), "-c:v", "copy", "-c:a", "aac", "-shortest", muxed], null, default);
        var doc = await Document(engine, muxed, new(0, 0, 48, 60));
        var output = Assert.Single((await engine.ExecuteAsync(new(VideoCropEngine.Operation, [muxed], new(), root, doc), null, default)).Outputs);
        using var check = await process.ProbeJsonAsync(["-show_streams", "-of", "json", ..MediaProcess.InputArguments(output)], 1024 * 1024, default);
        Assert.Equal(6, check.RootElement.GetProperty("streams").EnumerateArray().Single(s => s.GetProperty("codec_type").GetString() == "audio").GetProperty("channels").GetInt32());
    }

    [Fact]
    public async Task Oversize_crop_is_rejected_before_encoding()
    {
        var engine = registry.VideoCrop; Assert.NotNull(engine);
        var doc = await Document(engine, input, new(0, 0, 8192, 120));
        var issues = await engine.ValidateAsync(new(VideoCropEngine.Operation, [input], new(), root, doc), default);
        Assert.Contains(issues, issue => issue.Message.Contains("8190", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Rotated_and_non_square_pixel_crops_keep_display_coordinates_and_audio(bool rotated, int sar)
    {
        var engine = registry.VideoCrop; Assert.NotNull(engine);
        string wave = Path.Combine(root, "tone.wav");
        MediaFixture.WriteWave(wave, Enumerable.Range(0, 48000).Select(i => (short)(500 * Math.Sin(i * Math.PI * 440 / 24000))).ToArray());
        string anamorphic = Path.Combine(root, "sar.mp4"), selected = Path.Combine(root, "rotated.mp4");
        await process.RunAsync([..MediaProcess.InputArguments(input), ..MediaProcess.InputArguments(wave), "-vf", $"setsar={sar}", "-c:v", "mpeg4", "-q:v", "1", "-c:a", "aac", "-shortest", anamorphic], null, default);
        if (rotated) await process.RunAsync(["-display_rotation", "90", ..MediaProcess.InputArguments(anamorphic), "-c", "copy", selected], null, default);
        else selected = anamorphic;
        var crop = rotated ? new RectD(60, 48 * sar, 60, 48 * sar) : new RectD(48 * sar, 60, 48 * sar, 60);
        var doc = await Document(engine, selected, crop);
        var output = Assert.Single((await engine.ExecuteAsync(new(VideoCropEngine.Operation, [selected], new(), root, doc), null, default)).Outputs);
        using var metadata = await process.ProbeJsonAsync(["-show_streams", "-of", "json", ..MediaProcess.InputArguments(output)], 1024 * 1024, default);
        var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var picture = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
        Assert.Equal(crop.Width, picture.GetProperty("width").GetInt32()); Assert.Equal(crop.Height, picture.GetProperty("height").GetInt32());
        Assert.Contains(streams, s => s.GetProperty("codec_type").GetString() == "audio");
        string pixels = Path.Combine(root, "check.bgra");
        await process.RunAsync([..MediaProcess.InputArguments(output), "-frames:v", "1", "-c:v", "rawvideo", "-pix_fmt", "bgra", "-f", "rawvideo", pixels], null, default);
        int midpoint = 4 * ((int)crop.Height / 2 * (int)crop.Width + (int)crop.Width / 2);
        Assert.InRange(File.ReadAllBytes(pixels)[midpoint], (byte)(rotated ? 80 : 210), (byte)(rotated ? 100 : 230));
    }

    [Fact]
    public async Task Changed_original_and_cancellation_publish_no_output()
    {
        var engine = registry.VideoCrop; Assert.NotNull(engine);
        var document = await Document(engine, input, new(0, 0, 48, 60));
        var request = new OperationRequest(VideoCropEngine.Operation, [input], new(), root, document);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExecuteAsync(request, null, canceled.Token));
        }
        await File.AppendAllTextAsync(input, "source changed");
        await Assert.ThrowsAsync<IOException>(() => engine.ExecuteAsync(request, null, default));
        Assert.DoesNotContain(Directory.GetFiles(root), path => Path.GetFileName(path).StartsWith("result", StringComparison.Ordinal));
    }

    private async Task<EditorDocument> Document(VideoCropEngine engine, string path, RectD crop)
    {
        var source = (await engine.Sources.CaptureAsync([path], default)).Single().Snapshot!;
        var info = await engine.InspectAsync(source, default);
        return new(1, 0, [source], [], new VideoRecipe([new(Guid.NewGuid(), source.Id, info.Video[0].Index, info.Audio.FirstOrDefault()?.Index,
            new(new(0, 1), info.Duration), 1)], crop, new((int)crop.Width, (int)crop.Height), ImageFit.Contain, false, [], [], null, []),
            new("mp4", root, "result", new QualityGoal(85), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
    }

    [Fact]
    public async Task Crop_is_discoverable_and_exports_selected_pixels_without_modifying_original()
    {
        var operation = new OperationCatalog(registry.Engines).GetOperations([input], OperationMode.Tools).SingleOrDefault(o => o.Id == "video.crop");
        Assert.NotNull(operation);
        var engine = registry.Engines.Single(e => e.Operations.Any(o => o.Id == operation.Id));
        var source = (await new SourceSnapshotService().CaptureAsync([input], default)).Single().Snapshot!;
        var document = new EditorDocument(1, 3, [source], [],
            new VideoRecipe([new(Guid.NewGuid(), source.Id, 0, null, new(new(0, 1), new(1, 1)), 1)], new(48, 60, 48, 60), new(48, 60), ImageFit.Contain, false, [], [], null, []),
            new("mp4", root, "cropped", new QualityGoal(85), MetadataPolicy.StripAll, new(0, 0, 0, 255)));
        byte[] hash = SHA256.HashData(File.ReadAllBytes(input));
        var request = new OperationRequest(operation, [input], new(), root, document);
        var output = Assert.Single((await engine.ExecuteAsync(request, null, default)).Outputs);
        using var metadata = await process.ProbeJsonAsync(["-show_streams", "-of", "json", ..MediaProcess.InputArguments(output)], 1024 * 1024, default);
        var stream = metadata.RootElement.GetProperty("streams")[0];
        Assert.Equal(48, stream.GetProperty("width").GetInt32()); Assert.Equal(60, stream.GetProperty("height").GetInt32());
        string pixels = Path.Combine(root, "output.bgra");
        await process.RunAsync([..MediaProcess.InputArguments(output), "-frames:v", "1", "-c:v", "rawvideo", "-pix_fmt", "bgra", "-f", "rawvideo", pixels], null, default);
        Assert.InRange(File.ReadAllBytes(pixels)[4 * (30 * 48 + 24)], (byte)210, (byte)230);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(input)));
        string second = Assert.Single((await engine.ExecuteAsync(request, null, default)).Outputs);
        Assert.NotEqual(output, second); Assert.True(File.Exists(output));
    }
}
