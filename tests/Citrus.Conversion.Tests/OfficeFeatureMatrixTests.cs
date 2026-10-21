using System.IO.Compression;
using Citrus.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S=DocumentFormat.OpenXml.Spreadsheet;
using W=DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Citrus.Conversion.Tests;

public sealed class OfficeFeatureMatrixTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"citrus-office-matrix-"+Guid.NewGuid().ToString("N"));
    public OfficeFeatureMatrixTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>Directory.Delete(root,true);
    private string Pdf()
    {
        var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(400,300).AddText("Reference feature content",18,new PdfPoint(30,200),font);
        string path=Path.Combine(root,"reference.pdf");File.WriteAllBytes(path,builder.Build());return path;
    }
    private async Task<string> FromPdf(string id)
    { var engine=new PdfEngine();return Assert.Single((await engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id==id),[Pdf()],new(),null),null,default)).Outputs); }
    private async Task<ConversionResult> Run(string id,string input)
    { var engine=new DocumentConversionEngine();return await engine.ExecuteAsync(new(engine.Operations.Single(o=>o.Id==id),[input],new(),null),null,default); }
    [Theory]
    [InlineData("pdf")][InlineData("png")][InlineData("jpg")]
    public async Task Presentation_targets_reopen_and_keep_source(string format)
    {
        string input=await FromPdf("pdf.pptx");byte[] before=File.ReadAllBytes(input);var result=await Run("presentation.convert."+format,input);
        foreach(string path in result.Outputs)
        {if(format=="pdf"){using var pdf=UglyToad.PdfPig.PdfDocument.Open(path);Assert.Contains("Reference feature content",pdf.GetPage(1).Text);}else{using var image=new ImageMagick.MagickImage(path);Assert.True(image.Width>0);}}
        Assert.Equal(before,File.ReadAllBytes(input));
    }
    private string Workbook()
    {
        string path=Path.Combine(root,"sheets.xlsx");using var package=SpreadsheetDocument.Create(path,SpreadsheetDocumentType.Workbook);
        var book=package.AddWorkbookPart();book.Workbook=new S.Workbook();var sheets=new S.Sheets();book.Workbook.Append(sheets);
        for(uint index=1;index<=2;index++)
        {
            var part=book.AddNewPart<WorksheetPart>();part.Worksheet=new S.Worksheet(new S.SheetData(new S.Row(new S.Cell{DataType=S.CellValues.InlineString,InlineString=new(new S.Text("00123"))},new S.Cell{DataType=S.CellValues.Number,CellValue=new("42.5")})));
            sheets.Append(new S.Sheet{Id=book.GetIdOfPart(part),SheetId=index,Name=index==1?"North":"South"});part.Worksheet.Save();
        }
        book.Workbook.Save();return path;
    }
    [Theory]
    [InlineData("ods")][InlineData("csv")][InlineData("tsv")][InlineData("pdf")]
    public async Task Spreadsheet_targets_export_real_cells_and_all_delimited_sheets(string format)
    {
        string input=Workbook();byte[] before=File.ReadAllBytes(input);var result=await Run("spreadsheet.convert."+format,input);
        var files=result.Outputs.SelectMany(p=>Directory.Exists(p)?Directory.GetFiles(p):new[]{p}).ToArray();
        if(format is "csv" or "tsv")
        {
            Assert.Equal(2,files.Length);foreach(string file in files){string text=File.ReadAllText(file);Assert.Contains("00123",text);Assert.Contains(format=="csv"?",":"\t",text);}
        }
        else if(format=="ods"){using var zip=ZipFile.OpenRead(Assert.Single(files));Assert.NotNull(zip.GetEntry("content.xml"));}
        else {using var pdf=UglyToad.PdfPig.PdfDocument.Open(Assert.Single(files));Assert.True(pdf.NumberOfPages>=2);}
        Assert.Equal(before,File.ReadAllBytes(input));
    }
    [Theory]
    [InlineData("pdf")][InlineData("docx")][InlineData("txt")][InlineData("md")][InlineData("html")]
    public async Task Epub_targets_produce_readable_editable_copies(string format)
    {
        string input=await FromPdf("pdf.epub");byte[] before=File.ReadAllBytes(input);string output=Assert.Single((await Run("epub.convert."+format,input)).Outputs);
        if(format=="pdf"){using var pdf=UglyToad.PdfPig.PdfDocument.Open(output);Assert.Contains("Reference feature content",string.Join(' ',pdf.GetPages().Select(p=>p.Text)));}
        else if(format=="docx"){using var word=WordprocessingDocument.Open(output,false);Assert.Contains("Reference feature content",word.MainDocumentPart!.Document!.InnerText);}
        else Assert.Contains("Reference feature content",File.ReadAllText(output));Assert.Equal(before,File.ReadAllBytes(input));
    }
    [Theory]
    [InlineData("docx")][InlineData("md")]
    public async Task Remaining_document_destinations_are_genuine_formats(string format)
    {
        string input=await FromPdf("pdf.docx");string output=Assert.Single((await Run("document.convert."+format,input)).Outputs);
        if(format=="docx"){using var word=WordprocessingDocument.Open(output,false);Assert.Contains("Reference feature content",word.MainDocumentPart!.Document!.InnerText);}
        else Assert.Contains("Reference feature content",File.ReadAllText(output));
    }
}
