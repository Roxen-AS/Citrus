using Citrus.Core;

namespace Citrus.Core.Tests;

public sealed class ConversionWarningTests
{
    [Fact]
    public async Task Reconstruction_warnings_survive_success_and_are_frozen_at_completion()
    {
        var warningProperty = typeof(ConversionResult).GetProperty("Warnings");
        Assert.NotNull(warningProperty);
        var warnings = new List<string> { "Page 2: OCR text has low confidence." };
        var operation = new OperationDescriptor("pdf.docx", "DOCX", OperationMode.Convert, ".docx", false, false);
        var result = new ConversionResult(["converted.docx"]);
        warningProperty.SetValue(result, warnings);
        var engine = new TestEngine([operation], _ => true, (_, _, _) => Task.FromResult(result));
        var job = new JobQueue([engine]).Enqueue(new(operation, ["source.pdf"], new(), null));
        await job.Completion;
        Assert.Equal(ConversionJobState.Succeeded, job.State);
        var jobWarnings = typeof(ConversionJob).GetProperty("Warnings");
        Assert.NotNull(jobWarnings);
        var captured = Assert.IsAssignableFrom<IReadOnlyList<string>>(jobWarnings.GetValue(job));
        warnings.Clear();
        Assert.Equal("Page 2: OCR text has low confidence.", Assert.Single(captured));
    }
}
