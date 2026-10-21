using System.IO.Compression;
using System.Xml.Linq;
using Citrus.Core;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Citrus.Conversion.Tests;

public sealed class PdfReferenceExportsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "citrus-pdf-reference-" + Guid.NewGuid().ToString("N"));
    public PdfReferenceExportsTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string Input()
    {
        var builder = new PdfDocumentBuilder(); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(500, 700);
        page.AddText("Editable heading", 24, new PdfPoint(40, 620), font);
        page.AddText("Actual paragraph content", 12, new PdfPoint(40, 570), font);
        string path = Path.Combine(root, "source.pdf"); File.WriteAllBytes(path, builder.Build()); return path;
    }
    private static Task<ConversionResult> Run(PdfEngine engine, string id, string path) =>
        engine.ExecuteAsync(new(engine.Operations.Single(o => o.Id == id), [path], new(), null), null, default);

    [Theory]
    [InlineData("pdf.docx", "word/document.xml")]
    [InlineData("pdf.pptx", "ppt/slides/slide1.xml")]
    public async Task Office_exports_contain_editable_text_objects_not_full_page_bitmaps(string id, string member)
    {
        string input = Input(); byte[] original = File.ReadAllBytes(input);
        string output = Assert.Single((await Run(new(), id, input)).Outputs);
        using var zip = ZipFile.OpenRead(output); using var data = zip.GetEntry(member)!.Open();
        var xml = XDocument.Load(data);
        Assert.Contains("Editable heading", xml.ToString()); Assert.Contains("Actual paragraph content", xml.ToString());
        Assert.Equal(original, File.ReadAllBytes(input));
    }

    [Theory]
    [InlineData("pdf.markdown", ".md")]
    [InlineData("pdf.epub", ".epub")]
    [InlineData("pdf.jpg", ".jpg")]
    public async Task Additional_reference_exports_are_real_formats(string id, string extension)
    {
        string output = Assert.Single((await Run(new(), id, Input())).Outputs);
        Assert.Equal(extension, Path.GetExtension(output));
        if (extension == ".md") Assert.Contains("Actual paragraph content", File.ReadAllText(output));
        else if (extension == ".epub") { using var zip = ZipFile.OpenRead(output); using var reader = new StreamReader(zip.GetEntry("mimetype")!.Open()); Assert.Equal("application/epub+zip", reader.ReadToEnd()); Assert.NotNull(zip.GetEntry("META-INF/container.xml")); }
        else { using var image = new ImageMagick.MagickImage(output); Assert.Equal(ImageMagick.MagickFormat.Jpeg, image.Format); }
    }

    [Fact]
    public async Task Table_free_pdf_does_not_publish_empty_or_image_only_workbook()
    {
        string path = Input(); var engine = new PdfEngine();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Run(engine, "pdf.xlsx", path));
        Assert.Contains("No tables detected", error.Message); Assert.Equal(new[] { path }, Directory.GetFiles(root));
    }
}
