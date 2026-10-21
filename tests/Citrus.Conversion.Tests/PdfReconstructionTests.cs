using System.IO.Compression;
using System.Xml.Linq;
using Citrus.Core;
using Citrus.Core.Editing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Citrus.Conversion.Tests;

public sealed class PdfReconstructionTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"citrus-pdf-reconstruction-"+Guid.NewGuid().ToString("N"));
    public PdfReconstructionTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>Directory.Delete(root,true);
    private string Input(bool table)
    {
        var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);var page=builder.AddPage(400,300);
        if(table)
        {
            foreach(double y in new[] { 60d,110d,160d })page.DrawLine(new(30,y),new(350,y),1);
            foreach(double x in new[] { 30d,190d,350d })page.DrawLine(new(x,60),new(x,160),1);
            page.AddText("Identifier",12,new(40,130),font);page.AddText("Amount",12,new(200,130),font);
            page.AddText("00123",12,new(40,80),font);page.AddText("42.5",12,new(200,80),font);
        }
        else page.AddText("Readable scanned paragraph",18,new(30,200),font);
        string path=Path.Combine(root,Guid.NewGuid().ToString("N")+".pdf");File.WriteAllBytes(path,builder.Build());return path;
    }
    private static Task<ConversionResult> Run(string id,string input)
    {var engine=new PdfEngine();return engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id==id),[input],new(),null),null,default);}
    private async Task<string> Scan(string input)
    {string raster=Assert.Single((await Run("pdf.images",input)).Outputs);return Assert.Single((await Run("images.pdf",raster)).Outputs);}
    [Fact]
    public async Task Native_table_exports_actual_cells_and_preserves_identifier_zeroes()
    {
        string output=Assert.Single((await Run("pdf.xlsx",Input(true))).Outputs);using var package=SpreadsheetDocument.Open(output,false);
        string xml=package.WorkbookPart!.WorksheetParts.Single().Worksheet!.OuterXml;
        Assert.Contains("00123",xml);Assert.Contains("inlineStr",xml);Assert.Contains("42.5",xml);
        Assert.Empty(new OpenXmlValidator().Validate(package));
    }
    [Theory]
    [InlineData("pdf.docx",false)][InlineData("pdf.pptx",false)][InlineData("pdf.docx",true)][InlineData("pdf.pptx",true)]
    public async Task Reconstructed_office_packages_pass_schema_validation(string id,bool table)
    {
        string output=Assert.Single((await Run(id,Input(table))).Outputs);
        using OpenXmlPackage package=id=="pdf.docx"?WordprocessingDocument.Open(output,false):PresentationDocument.Open(output,false);
        Assert.Empty(new OpenXmlValidator().Validate(package));
    }
    [Fact]
    public async Task Scanned_page_is_reconstructed_as_editable_ocr_text_with_diagnostic()
    {
        string scanned=await Scan(Input(false));var result=await Run("pdf.docx",scanned);string output=Assert.Single(result.Outputs);
        using var package=WordprocessingDocument.Open(output,false);Assert.Contains("Readable scanned paragraph",package.MainDocumentPart!.Document!.InnerText);
        Assert.Contains(result.Warnings!,w=>w.Contains("OCR",StringComparison.Ordinal));
    }
    [Fact]
    public async Task Scanned_text_destination_uses_the_same_OCR_model()
    {
        string scanned=await Scan(Input(false));var result=await Run("pdf.text",scanned);
        Assert.Contains("Readable scanned paragraph",await File.ReadAllTextAsync(Assert.Single(result.Outputs)));
        Assert.Contains(result.Warnings!,warning=>warning.Contains("OCR",StringComparison.Ordinal));
    }
    [Fact]
    public async Task Native_heading_and_partial_scanned_region_both_remain_editable()
    {
        byte[] png=await PdfEngine.RenderPageAsync(Input(false),1,150,default);
        var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);var page=builder.AddPage(500,500);
        page.AddText("Native heading",18,new PdfPoint(30,460),font);page.AddPng(png,new PdfRectangle(30,50,430,350));
        string input=Path.Combine(root,"mixed.pdf");await File.WriteAllBytesAsync(input,builder.Build());
        var result=await Run("pdf.docx",input);using var package=WordprocessingDocument.Open(Assert.Single(result.Outputs),false);
        string text=package.MainDocumentPart!.Document!.InnerText;Assert.Contains("Native heading",text);Assert.Contains("Readable scanned paragraph",text);
        Assert.Equal(1,text.Split("Native heading",StringSplitOptions.None).Length-1);
        Assert.Contains(result.Warnings!,warning=>warning.Contains("OCR",StringComparison.Ordinal));
    }
    [Fact]
    public async Task Markdown_artwork_bundle_rolls_back_on_late_publication_failure()
    {
        var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);var page=builder.AddPage(400,300);
        page.AddText("Native content",18,new PdfPoint(20,250),font);
        using(var image=new ImageMagick.MagickImage(ImageMagick.MagickColors.CornflowerBlue,40,40))page.AddPng(image.ToByteArray(ImageMagick.MagickFormat.Png),new PdfRectangle(20,20,80,80));
        string input=Path.Combine(root,"artwork.pdf");await File.WriteAllBytesAsync(input,builder.Build());var engine=new PdfEngine();
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id=="pdf.markdown"),[input],new(),null),new LateFailure(),default));
        Assert.Equal("Late publication failure",failure.Message);Assert.Equal(new[]{input},Directory.GetFileSystemEntries(root));
    }
    [Fact]
    public async Task Image_only_PDF_preserves_artwork_when_OCR_has_no_text_to_replace_it()
    {
        var builder=new PdfDocumentBuilder();var page=builder.AddPage(200,150);
        using(var image=new ImageMagick.MagickImage(ImageMagick.MagickColors.CornflowerBlue,400,300))page.AddPng(image.ToByteArray(ImageMagick.MagickFormat.Png),new PdfRectangle(0,0,200,150));
        string input=Path.Combine(root,"photo.pdf");await File.WriteAllBytesAsync(input,builder.Build());
        var result=await Run("pdf.docx",input);using var output=WordprocessingDocument.Open(Assert.Single(result.Outputs),false);
        Assert.Single(output.MainDocumentPart!.ImageParts);
    }
    private sealed class LateFailure:IProgress<ConversionProgress>
    {public void Report(ConversionProgress progress){if(progress.Message=="Copies ready")throw new InvalidOperationException("Late publication failure");}}
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Typed_text_export_keeps_table_cells_and_OCR_job_diagnostics(bool scanned)
    {
        string path=Input(true);if(scanned)path=await Scan(path);var engine=new PdfEngine();var source=(await engine.Sources.CaptureAsync([path],default)).Single().Snapshot!;
        var recipe=new PdfRecipe([new(Guid.NewGuid(),source.Id,1,0)],PdfOutputMode.Text,150,PdfCompressionMode.PreserveStructure,85,[]);
        var document=new EditorDocument(1,0,[source],[],recipe,new("txt",root,"table-text",new QualityGoal(85),MetadataPolicy.PreserveSupported,new(255,255,255,255)));
        var queue=new JobQueue([engine]);var job=queue.Enqueue(new(engine.Operations.Single(o=>o.Id=="pdf.text"),[path],new(),root,document));await job.Completion;
        Assert.Equal(ConversionJobState.Succeeded,job.State);string text=await File.ReadAllTextAsync(Assert.Single(job.Outputs));Assert.Contains("Identifier",text);Assert.Contains("00123",text);Assert.Contains("42.5",text);
        if(scanned)Assert.Contains(job.Warnings,w=>w.Contains("OCR",StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Text_reading_order_preserves_explicit_page_order_before_coordinates(bool reverse)
    {
        var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(400,300).AddText("First page body",14,new PdfPoint(20,80),font);builder.AddPage(400,300).AddText("Second page heading",14,new PdfPoint(20,250),font);
        string input=Path.Combine(root,"ordered-text.pdf");await File.WriteAllBytesAsync(input,builder.Build());var engine=new PdfEngine();var operation=engine.Operations.Single(o=>o.Id=="pdf.text");
        ConversionResult result;
        if(reverse)
        {
            var source=(await engine.Sources.CaptureAsync([input],default)).Single().Snapshot!;
            var recipe=new PdfRecipe([new(Guid.NewGuid(),source.Id,2,0),new(Guid.NewGuid(),source.Id,1,0)],PdfOutputMode.Text,150,PdfCompressionMode.PreserveStructure,85,[]);
            var document=new EditorDocument(1,0,[source],[],recipe,new("txt",root,"reversed-text",new QualityGoal(85),MetadataPolicy.PreserveSupported,new(255,255,255,255)));
            result=await engine.ExecuteAsync(new(operation,[input],new(),root,document),null,default);
        }
        else result=await Run("pdf.text",input);
        string text=await File.ReadAllTextAsync(Assert.Single(result.Outputs));int first=text.IndexOf("First page body",StringComparison.Ordinal),second=text.IndexOf("Second page heading",StringComparison.Ordinal);
        Assert.True(first>=0&&second>=0);Assert.True(reverse?second<first:first<second,text);
    }
    [Fact]
    public async Task Scanned_ruled_table_is_reconstructed_as_cells()
    {
        string scanned=await Scan(Input(true));
        string proof=Path.Combine(Directory.GetParent(FeaturePackLocator.Find())!.Parent!.FullName,".local","document-fixtures");Directory.CreateDirectory(proof);File.Copy(scanned,Path.Combine(proof,"scanned-table.pdf"),true);
        var model=await Citrus.Conversion.Editing.Pdf.PdfSemanticExtractor.ExtractAsync(scanned,[1],FeaturePackLocator.Find(),default);
        await File.WriteAllTextAsync(Path.Combine(proof,"scanned-table-model.json"),System.Text.Json.JsonSerializer.Serialize(model));
        await File.WriteAllBytesAsync(Path.Combine(proof,"scanned-table-render.png"),await PdfEngine.RenderPageAsync(scanned,1,300,default));
        string output=Assert.Single((await Run("pdf.xlsx",scanned)).Outputs);
        using var package=SpreadsheetDocument.Open(output,false);Assert.Contains("00123",package.WorkbookPart!.WorksheetParts.Single().Worksheet!.InnerText);
    }
}
