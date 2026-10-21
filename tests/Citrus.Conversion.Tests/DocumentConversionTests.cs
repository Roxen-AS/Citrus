using System.IO.Compression;
using Citrus.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W=DocumentFormat.OpenXml.Wordprocessing;

namespace Citrus.Conversion.Tests;

public sealed class DocumentConversionTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"citrus-office-reference-"+Guid.NewGuid().ToString("N"));
    public DocumentConversionTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>Directory.Delete(root,true);
    private string Input()
    {
        string path=Path.Combine(root,"source.docx");using var doc=WordprocessingDocument.Create(path,WordprocessingDocumentType.Document);
        doc.AddMainDocumentPart().Document=new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("Offline editable source")))));return path;
    }
    private IConversionEngine Engine()
    {
        var type=typeof(PdfEngine).Assembly.GetType("Citrus.Conversion.DocumentConversionEngine");Assert.NotNull(type);
        return Assert.IsAssignableFrom<IConversionEngine>(Activator.CreateInstance(type,[null,null]));
    }
    [Theory]
    [InlineData("pdf")][InlineData("txt")][InlineData("html")][InlineData("epub")][InlineData("png")][InlineData("jpg")]
    public async Task Offline_document_exports_are_real_and_preserve_original(string format)
    {
        string input=Input();byte[] original=File.ReadAllBytes(input);var engine=Engine();
        var op=engine.Operations.Single(o=>o.Id=="document.convert."+format);
        Assert.True(engine.CanExecute(new(op,[input],new(),null)));
        var result=await engine.ExecuteAsync(new(op,[input],new(),null),null,default);Assert.NotEmpty(result.Outputs);
        foreach(string path in result.Outputs)
        {
            if(format=="pdf") { using var pdf=UglyToad.PdfPig.PdfDocument.Open(path);Assert.Contains("Offline editable source",pdf.GetPage(1).Text); }
            else if(format=="txt"||format=="html") Assert.Contains("Offline editable source",File.ReadAllText(path));
            else if(format=="epub") { using var zip=ZipFile.OpenRead(path);Assert.NotNull(zip.GetEntry("mimetype")); }
            else { using var image=new ImageMagick.MagickImage(path);Assert.True(image.Width>0&&image.Height>0); }
        }
        Assert.Equal(original,File.ReadAllBytes(input));
    }
    [Fact]
    public async Task Canceled_document_conversion_creates_no_output()
    {
        string input=Input();var engine=Engine();var op=engine.Operations.Single(o=>o.Id=="document.convert.pdf");using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>engine.ExecuteAsync(new(op,[input],new(),null),null,cancel.Token));
        Assert.Equal(new[] { input },Directory.GetFiles(root));
    }
    [Fact]
    public async Task Published_document_copy_rolls_back_if_the_final_notification_fails()
    {
        string input=Input();byte[] original=File.ReadAllBytes(input);var engine=Engine();var operation=engine.Operations.Single(o=>o.Id=="document.convert.txt");
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>engine.ExecuteAsync(new(operation,[input],new(),null),new FinalFailure(),default));
        Assert.Equal("Final notification failed",failure.Message);Assert.Equal(new[]{input},Directory.GetFiles(root));Assert.Equal(original,File.ReadAllBytes(input));
    }
    private sealed class FinalFailure:IProgress<ConversionProgress>
    {public void Report(ConversionProgress progress){if(progress.Message=="Converted copy ready")throw new InvalidOperationException("Final notification failed");}}
}
