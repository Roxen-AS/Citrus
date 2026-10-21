using Citrus.Core;
using PdfSharp.Pdf;

namespace Citrus.Conversion.Tests;

public sealed class InterfaceIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Citrus interface " + Guid.NewGuid().ToString("N"));
    public InterfaceIntegrationTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task Missing_media_preserves_other_engines_with_reinstall_message()
    {
        var registry = await EngineRegistry.Create(new OutputWriter(), root);
        using var images = Assert.IsType<ImageEditorServices>(registry.ImageEditors);
        Assert.Contains(registry.Engines,engine=>engine is ImageEngine);
        Assert.Contains(registry.Engines,engine=>engine is PdfEngine);
        Assert.Contains(registry.Engines,engine=>engine is SubtitleEngine);
        Assert.Contains(registry.Engines,engine=>engine is ArchiveEngine);
        Assert.Null(registry.MediaEditors);
        Assert.Contains(images.Engine, registry.Engines);
        Assert.True(images.Routes.TryGet("image.crop", out _));
        Assert.Contains(registry.Warnings,warning=>warning.Contains("Audio and video",StringComparison.Ordinal)&&warning.Contains("reinstall",StringComparison.OrdinalIgnoreCase));
        string input = Path.Combine(root, "hello.srt");
        await File.WriteAllTextAsync(input, "1\n00:00:00,000 --> 00:00:01,000\nHello\n");
        var operation = Assert.Single(new OperationCatalog(registry.Engines).GetOperations([input], OperationMode.Convert), o => o.Id == "subtitle.vtt");
        var queue = new JobQueue(registry.Engines);
        var job = queue.Enqueue(new(operation, [input], new(), null));
        await job.Completion;
        Assert.Equal(ConversionJobState.Succeeded, job.State);
        Assert.StartsWith("WEBVTT", await File.ReadAllTextAsync(Assert.Single(job.Outputs)));
    }

    [Fact]
    public async Task Shared_submission_preserves_real_subtitle_partial_success()
    {
        string good = Path.Combine(root, "good.srt"), bad = Path.Combine(root, "bad.srt");
        await File.WriteAllTextAsync(good, "1\n00:00:00,000 --> 00:00:01,000\nHello\n");
        await File.WriteAllTextAsync(bad, "invalid subtitle");
        var engine = new SubtitleEngine();
        var service = new SubmissionService(new JobQueue([engine]), (_, _) => Task.FromResult<IReadOnlyList<InputInspection>>([]));
        var jobs = await service.SubmitAsync(engine.Operations.Single(o => o.Id == "subtitle.vtt"), [good, bad], (_, _) => throw new InvalidOperationException("Plain conversion needs no dialog"));
        await Task.WhenAll(jobs.Select(j => j.Completion));
        Assert.Equal(ConversionJobState.Succeeded, jobs[0].State); Assert.Equal(ConversionJobState.Failed, jobs[1].State);
        Assert.StartsWith("WEBVTT", await File.ReadAllTextAsync(Assert.Single(jobs[0].Outputs)));
        Assert.Equal("invalid subtitle", await File.ReadAllTextAsync(bad));
        Assert.Equal(3, Directory.GetFiles(root).Length);
    }

    [Fact]
    public void Real_catalog_intersects_mixed_file_kinds_and_retains_archive_groups()
    {
        string image = Path.Combine(root, "image.png"), subtitle = Path.Combine(root, "text.srt");
        File.WriteAllText(image, "catalog uses metadata"); File.WriteAllText(subtitle, "catalog uses metadata");
        var catalog = new OperationCatalog([new ImageEngine(), new PdfEngine(), new SubtitleEngine(), new ArchiveEngine()]);
        Assert.Equal(new[] { "archive.zip", "archive.tar", "archive.gzip" }, catalog.GetOperations([image, subtitle], OperationMode.Convert).Select(o => o.Id));
        Assert.Empty(catalog.GetOperations([image, subtitle], OperationMode.Tools));
        Assert.Equal("GIF still image", catalog.GetOperations([image], OperationMode.Convert).Single(o => o.Id == "image.convert.gif").Label);
    }

    [Fact]
    public async Task Pdf_inspection_reads_real_counts_and_keeps_corruption_item_specific()
    {
        string good = Path.Combine(root, "good.pdf"), bad = Path.Combine(root, "bad.pdf");
        using (var document = new PdfDocument()) { document.AddPage(); document.AddPage(); document.Save(good); }
        await File.WriteAllTextAsync(bad, "not a PDF");
        var inspector = new InputInspector(new MediaProcess(Path.Combine(root, "missing"), Path.Combine(root, "missing")));
        var op = new PdfEngine().Operations.Single(o => o.Id == "pdf.split");
        var inspections = await inspector.InspectAsync(new(op, [good, bad], new(), null, []), CancellationToken.None);
        Assert.Equal(2, inspections[0].PageCount); Assert.Null(inspections[0].Error);
        Assert.NotNull(inspections[1].Error);
        Assert.NotNull(OperationOptionsValidator.GetError(op, new() { PageSelection = "3" }, inspections));
        var service = new SubmissionService(new JobQueue([new PdfEngine()]), inspector.InspectAsync);
        var jobs = await service.SubmitAsync(op, [good, bad], (draft, _) => Task.FromResult<SubmissionChoice?>(new(draft.Inputs, new() { PageSelection = "2" }, null)));
        await Task.WhenAll(jobs.Select(j => j.Completion));
        Assert.Equal(ConversionJobState.Succeeded, jobs[0].State); Assert.Equal(ConversionJobState.Failed, jobs[1].State);
        Assert.Single(jobs[0].Outputs);
    }

    [Fact]
    public async Task Media_inspection_uses_real_probe_duration()
    {
        string wav = Path.Combine(root, "tone.wav");
        // One second of PCM silence, generated without an encoder dependency.
        using (var stream = File.Create(wav))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8); writer.Write(36 + 16000); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(16000); writer.Write(new byte[16000]);
        }
        var process = new MediaProcess(Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG") ?? "/usr/bin/ffmpeg", Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE") ?? "/usr/bin/ffprobe");
        var op = new OperationDescriptor("audio.trim", "Trim", OperationMode.Tools, ".wav", false, true);
        var inspections = await new InputInspector(process).InspectAsync(new(op, [wav], new(), null, []), CancellationToken.None);
        Assert.Null(inspections[0].Error); Assert.InRange(inspections[0].DurationSeconds!.Value, .99, 1.01);
        Assert.NotNull(OperationOptionsValidator.GetError(op, new() { EndSeconds = 2 }, inspections));
    }
}
