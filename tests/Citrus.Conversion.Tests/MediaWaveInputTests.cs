using System.Buffers.Binary;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Media;
using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Tests;

public sealed class MediaWaveInputTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Citrus-wave-input-" + Guid.NewGuid().ToString("N"));
    private readonly MediaProcess process = new(Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG") ?? "/usr/bin/ffmpeg", Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE") ?? "/usr/bin/ffprobe");
    public MediaWaveInputTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Canceled_probe_does_not_open_input_for_format_detection()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.ProbeAsync(
            Path.Combine(directory, "missing.wav"), new CancellationToken(canceled: true)));
    }

    [Theory]
    [InlineData("wav")]
    [InlineData("dat")]
    public async Task Wave_signature_preserves_periodic_pcm_that_resembles_mpegts(string extension)
    {
        string path = Path.Combine(directory, "periodic." + extension);
        short[] samples = Enumerable.Range(0, 4800).Select(i => (short)(12000 * Math.Sin(i * Math.PI / 24))).ToArray();
        MediaFixture.WriteWave(path, samples);
        byte[] original = await File.ReadAllBytesAsync(path);
        var legacy = await process.ProbeAsync(path, default);
        Assert.True(legacy.HasAudio); Assert.False(legacy.HasVideo);
        Assert.Equal(.1, legacy.DurationSeconds, 8);
        await AssertPreviewSamples(path, samples);
        if (extension == "wav")
        {
            var engine = await MediaEngine.CreateAsync(process);
            var trim = Assert.Single(engine.Operations, o => o.Id == "audio.trim");
            var inspection = Assert.Single(await new InputInspector(process).InspectAsync(new(trim, [path], new(), null, []), default));
            Assert.Null(inspection.Error); Assert.Equal(.1, inspection.DurationSeconds);
            var convert = Assert.Single(engine.Operations, o => o.Id == "media.convert.flac");
            var converted = await engine.ExecuteAsync(new(convert, [path], new(), null), null, default);
            await AssertPreviewSamples(Assert.Single(converted.Outputs), samples);
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Non_wave_content_with_wav_extension_keeps_native_autodetection()
    {
        string input = Path.Combine(directory, "input.wav"), disguised = Path.Combine(directory, "flac.wav");
        short[] samples = Enumerable.Repeat((short)12345, 9600).ToArray();
        MediaFixture.WriteWave(input, samples);
        await process.RunAsync(["-i", input, "-c:a", "flac", "-f", "flac", disguised], null, default);
        Assert.Equal("fLaC"u8.ToArray(), (await File.ReadAllBytesAsync(disguised)).Take(4));
        Assert.True((await process.ProbeAsync(disguised, default)).HasAudio);
        await AssertPreviewSamples(disguised, samples);
    }

    private async Task AssertPreviewSamples(string path, short[] expected)
    {
        var snapshots = new SourceSnapshotService();
        var source = (await snapshots.CaptureAsync([path], default))[0].Snapshot!;
        await using var probe = new MediaProbe(process, snapshots, Path.Combine(directory, Guid.NewGuid().ToString("N")));
        var info = await probe.InspectAsync(source, default);
        Assert.Equal(new RationalTime(expected.Length, 48000), info.Duration);
        var stream = Assert.Single(info.Audio);
        Assert.Equal(0, stream.RawStartPts); Assert.Equal(new RationalTime(0, 1), info.PresentationOrigin);
        var clip = new MediaClip(Guid.NewGuid(), source.Id, null, stream.Index, new(new(0, 1), info.Duration), 1);
        var document = new EditorDocument(1, 1, [source], [], new AudioRecipe([clip], 48000, new(["FC"], ["FC"], [1], false), [], null, []), new("wav", null, "audio", new QualityGoal(80), MetadataPolicy.StripAll, new()));
        var plan = new MediaGraphCompiler(probe, await process.DiscoverCapabilitiesAsync(default)).Compile(document, RenderPurpose.Preview, new(1, new(16, 8), null, null, null), probe.StagedSources);
        var pcm = await new MediaBinaryDecoder(process).DecodeAudioAsync(plan, Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pcm"), default);
        Assert.Equal(expected.Length, pcm.SampleFrames);
        byte[] bytes = await File.ReadAllBytesAsync(pcm.Path);
        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(i => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2))));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
