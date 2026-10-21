using System.Security.Cryptography;
using Citrus.Core;
using Citrus.Conversion.Editing.Pdf;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Citrus.Conversion.Tests;

/// <summary>Opt-in relocation proof. It does not establish clean-machine acceptance.</summary>
public sealed class PublishedFeaturePackSmokeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "citrus-published-pack-" + Guid.NewGuid().ToString("N"));

    public PublishedFeaturePackSmokeTests() => Directory.CreateDirectory(root);
    public void Dispose() => OfflineStagingFiles.DeletePrivateTree(root);

    [PublishedFeaturePackFact]
    public async Task Published_pack_converts_fresh_docx_to_independently_reopened_pdf_without_changing_source()
    {
        string pack = Environment.GetEnvironmentVariable("CITRUS_TEST_PUBLISHED_FEATURE_PACK")!;
        Assert.True(Path.IsPathFullyQualified(pack), "The opt-in published feature-pack path must be absolute.");
        var engine = new DocumentConversionEngine(featurePackDirectory: pack);
        Assert.True(engine.HasRequiredPackFiles, "The explicitly selected published feature pack is incomplete.");

        const string marker = "CitrusPublishedPackRelocation";
        string input = Path.Combine(root, "fresh-source.docx");
        using (var source = WordprocessingDocument.Create(input, WordprocessingDocumentType.Document))
            source.AddMainDocumentPart().Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text(marker)))));
        byte[] sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(input));
        string destination = Path.Combine(root, "published-output");
        Directory.CreateDirectory(destination);

        var operation = engine.Operations.Single(candidate => candidate.Id == "document.convert.pdf");
        var result = await engine.ExecuteAsync(new OperationRequest(operation, [input], new(), destination), null, default);
        string output = Assert.Single(result.Outputs);
        Assert.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", output, StringComparison.OrdinalIgnoreCase);
        using (var reopened = UglyToad.PdfPig.PdfDocument.Open(output))
        {
            Assert.False(reopened.IsEncrypted);
            Assert.Equal(1, reopened.NumberOfPages);
            Assert.Contains(marker, reopened.GetPage(1).Text, StringComparison.Ordinal);
        }
        using (var independent = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            Assert.Equal(1, independent.PageCount);
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(input)));
        using var original = WordprocessingDocument.Open(input, false);
        Assert.Contains(marker, original.MainDocumentPart!.Document!.Body!.InnerText, StringComparison.Ordinal);
    }
}

public sealed class PublishedFeaturePackFactAttribute : FactAttribute
{
    public PublishedFeaturePackFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CITRUS_TEST_PUBLISHED_FEATURE_PACK")))
            Skip = "Set an absolute CITRUS_TEST_PUBLISHED_FEATURE_PACK to opt into the published runtime relocation smoke test.";
    }
}
