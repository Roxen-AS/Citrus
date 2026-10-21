using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W=DocumentFormat.OpenXml.Wordprocessing;
using S=DocumentFormat.OpenXml.Spreadsheet;

namespace Citrus.Conversion.Editing.Pdf;

public static class PdfSemanticExport
{
    public static Task WriteAsync(PdfSemanticDocument document,string extension,string output,CancellationToken ct)=>Task.Run(()=>
    {
        ct.ThrowIfCancellationRequested();
        switch(extension)
        {
            case ".docx": Word(document,output,ct); break;
            case ".pptx": Slides(document,output,ct); break;
            case ".xlsx": Workbook(document,output,ct); break;
            case ".epub": Epub(document,output,ct); break;
            case ".txt": File.WriteAllText(output,string.Join("\n\n",document.Pages.SelectMany(PageText)),new UTF8Encoding(false)); break;
            case ".md": File.WriteAllText(output,Markdown(document),new UTF8Encoding(false)); break;
            default: throw new NotSupportedException("Unsupported semantic PDF output.");
        }
        ct.ThrowIfCancellationRequested();
    },ct);

    private static IEnumerable<string> PageText(PdfSemanticPage page)=>page.Text.Select(text=>(Y:text.Bounds.Y,X:text.Bounds.X,Text:text.Text))
        .Concat(page.Tables.Select(table=>(Y:table.Bounds.Y,X:table.Bounds.X,Text:string.Join('\n',table.Rows.Select(row=>string.Join('\t',row))))))
        .OrderBy(item=>item.Y).ThenBy(item=>item.X).Select(item=>item.Text);

    private static void Word(PdfSemanticDocument document,string path,CancellationToken ct)
    {
        using var package=WordprocessingDocument.Create(path,WordprocessingDocumentType.Document);
        var main=package.AddMainDocumentPart(); var body=new W.Body(); main.Document=new W.Document(body);
        foreach(var page in document.Pages)
        {
            ct.ThrowIfCancellationRequested();
            if(body.ChildElements.Count>0) body.Append(new W.Paragraph(new W.Run(new W.Break { Type=W.BreakValues.Page })));
            foreach(var line in page.Text)
            {
                var properties=new W.RunProperties(new W.FontSize { Val=Math.Round(line.FontSize*2).ToString(CultureInfo.InvariantCulture) });
                if(line.Bold) properties.Append(new W.Bold());
                body.Append(new W.Paragraph(new W.Run(properties,new W.Text(line.Text) { Space=SpaceProcessingModeValues.Preserve })));
            }
            foreach(var table in page.Tables)
            {
                int columns=table.Rows.Max(row=>row.Length);
                var grid=new W.Table(new W.TableProperties(new W.TableWidth { Width="5000",Type=W.TableWidthUnitValues.Pct }),new W.TableGrid(Enumerable.Range(0,columns).Select(_=>new W.GridColumn())));
                foreach(var row in table.Rows) grid.Append(new W.TableRow(row.Select(cell=>new W.TableCell(new W.Paragraph(new W.Run(new W.Text(cell) { Space=SpaceProcessingModeValues.Preserve }))))));
                body.Append(grid);
            }
            foreach(var image in page.Images)
            {
                var part=main.AddImagePart(ImagePartType.Png); using(var data=new MemoryStream(image.Png)) part.FeedData(data);
                string id=main.GetIdOfPart(part); long width=(long)Math.Clamp(image.Bounds.Width*12700,12700,5_500_000),height=(long)Math.Clamp(image.Bounds.Height*12700,12700,8_000_000);
                var drawing=new W.Drawing { InnerXml=$"<wp:inline xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:pic='http://schemas.openxmlformats.org/drawingml/2006/picture' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><wp:extent cx='{width}' cy='{height}'/><wp:docPr id='{body.ChildElements.Count+1}' name='Recovered artwork'/><a:graphic><a:graphicData uri='http://schemas.openxmlformats.org/drawingml/2006/picture'><pic:pic><pic:nvPicPr><pic:cNvPr id='0' name='Recovered artwork'/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed='{id}'/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x='0' y='0'/><a:ext cx='{width}' cy='{height}'/></a:xfrm><a:prstGeom prst='rect'><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline>" };
                body.Append(new W.Paragraph(new W.Run(drawing)));
            }
        }
        main.Document.Save(); package.Dispose();
        using var verification=WordprocessingDocument.Open(path,false);
        if(verification.MainDocumentPart?.Document?.Body is null) throw new InvalidDataException("The editable Word copy could not be reopened.");
    }

    private static void Workbook(PdfSemanticDocument document,string path,CancellationToken ct)
    {
        var tables=document.Pages.SelectMany(p=>p.Tables.Select((t,i)=>(Page:p.Number,Index:i+1,Table:t))).ToArray();
        if(tables.Length==0) throw new InvalidDataException("No tables detected. No workbook was saved.");
        using var package=SpreadsheetDocument.Create(path,SpreadsheetDocumentType.Workbook);
        var book=package.AddWorkbookPart(); book.Workbook=new S.Workbook(); var sheets=new S.Sheets(); book.Workbook.Append(sheets); uint id=1;
        foreach(var item in tables)
        {
            ct.ThrowIfCancellationRequested(); var sheet=book.AddNewPart<WorksheetPart>(); var data=new S.SheetData(); sheet.Worksheet=new S.Worksheet(data);
            foreach(var row in item.Table.Rows)
            {
                var cells=new S.Row();
                foreach(string value in row)
                {
                    bool number=value.Length>0 && !(value.Length>1 && value[0]=='0' && char.IsDigit(value[1])) && double.TryParse(value,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var parsed) && double.IsFinite(parsed);
                    cells.Append(number?new S.Cell { DataType=S.CellValues.Number,CellValue=new(value) }:new S.Cell { DataType=S.CellValues.InlineString,InlineString=new(new S.Text(value) { Space=SpaceProcessingModeValues.Preserve }) });
                }
                data.Append(cells);
            }
            sheet.Worksheet.Save(); sheets.Append(new S.Sheet { Id=book.GetIdOfPart(sheet),SheetId=id++,Name=$"Page {item.Page} Table {item.Index}" });
        }
        book.Workbook.Save();
    }

    private static readonly XNamespace A="http://schemas.openxmlformats.org/drawingml/2006/main",P="http://schemas.openxmlformats.org/presentationml/2006/main",R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static void Slides(PdfSemanticDocument document,string path,CancellationToken ct)
    {
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        double width=document.Pages[0].Width,height=document.Pages[0].Height;
        var ids=new XElement(P+"sldIdLst"); var relationships=new List<(string,string,string)> { ("master",Rel("slideMaster"),"slideMasters/slideMaster1.xml") };
        var types=new List<(string,string)> { ("/ppt/presentation.xml","application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"),("/ppt/slideMasters/slideMaster1.xml","application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"),("/ppt/slideLayouts/slideLayout1.xml","application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml") };
        int index=0,imageIndex=0;
        foreach(var page in document.Pages)
        {
            ct.ThrowIfCancellationRequested(); index++; string relation="slide"+index; ids.Add(new XElement(P+"sldId",new XAttribute("id",255+index),new XAttribute(R+"id",relation)));
            relationships.Add((relation,Rel("slide"),$"slides/slide{index}.xml")); types.Add(($"/ppt/slides/slide{index}.xml","application/vnd.openxmlformats-officedocument.presentationml.slide+xml"));
            var tree=ShapeTree(); uint shapeId=2; var links=new List<(string,string,string)> { ("layout",Rel("slideLayout"),"../slideLayouts/slideLayout1.xml") };
            foreach(var text in page.Text)
            {
                tree.Add(new XElement(P+"sp",new XElement(P+"nvSpPr",new XElement(P+"cNvPr",new XAttribute("id",shapeId++),new XAttribute("name","Editable text")),new XElement(P+"cNvSpPr",new XAttribute("txBox",1)),new XElement(P+"nvPr")),
                    new XElement(P+"spPr",Transform(text.Bounds,page.Width,page.Height,width,height),new XElement(A+"prstGeom",new XAttribute("prst","rect"),new XElement(A+"avLst")),new XElement(A+"noFill")),
                    new XElement(P+"txBody",new XElement(A+"bodyPr",new XAttribute("wrap","none"),new XAttribute("lIns",0),new XAttribute("rIns",0),new XAttribute("tIns",0),new XAttribute("bIns",0)),new XElement(A+"lstStyle"),
                        new XElement(A+"p",new XElement(A+"r",new XElement(A+"rPr",new XAttribute("lang","en-US"),new XAttribute("sz",(int)(text.FontSize*100)),new XAttribute("b",text.Bold?1:0)),new XElement(A+"t",text.Text))))));
            }
            foreach(var table in page.Tables)
            {
                var graphic=new XElement(A+"tbl",new XElement(A+"tblPr"),new XElement(A+"tblGrid",Enumerable.Range(0,table.Rows.Max(r=>r.Length)).Select(_=>new XElement(A+"gridCol",new XAttribute("w",(long)(table.Bounds.Width*12700/table.Rows.Max(r=>r.Length)))))));
                foreach(var row in table.Rows) graphic.Add(new XElement(A+"tr",new XAttribute("h",(long)(table.Bounds.Height*12700/table.Rows.Length)),row.Select(cell=>new XElement(A+"tc",new XElement(A+"txBody",new XElement(A+"bodyPr"),new XElement(A+"lstStyle"),new XElement(A+"p",new XElement(A+"r",new XElement(A+"t",cell)))),new XElement(A+"tcPr")))));
                tree.Add(new XElement(P+"graphicFrame",new XElement(P+"nvGraphicFramePr",new XElement(P+"cNvPr",new XAttribute("id",shapeId++),new XAttribute("name","Editable table")),new XElement(P+"cNvGraphicFramePr"),new XElement(P+"nvPr")),new XElement(P+"xfrm",Transform(table.Bounds,page.Width,page.Height,width,height).Elements()),new XElement(A+"graphic",new XElement(A+"graphicData",new XAttribute("uri",A.NamespaceName+"/table"),graphic))));
            }
            foreach(var image in page.Images)
            {
                imageIndex++; string name=$"image{imageIndex}.png",id="image"+imageIndex; links.Add((id,Rel("image"),"../media/"+name));
                var entry=zip.CreateEntry("ppt/media/"+name); using(var sink=entry.Open()) sink.Write(image.Png);
                tree.Add(new XElement(P+"pic",new XElement(P+"nvPicPr",new XElement(P+"cNvPr",new XAttribute("id",shapeId++),new XAttribute("name","Recovered artwork")),new XElement(P+"cNvPicPr"),new XElement(P+"nvPr")),new XElement(P+"blipFill",new XElement(A+"blip",new XAttribute(R+"embed",id)),new XElement(A+"stretch",new XElement(A+"fillRect"))),new XElement(P+"spPr",Transform(image.Bounds,page.Width,page.Height,width,height),new XElement(A+"prstGeom",new XAttribute("prst","rect"),new XElement(A+"avLst")))));
            }
            Xml(zip,$"ppt/slides/slide{index}.xml",new XElement(P+"sld",new XAttribute(XNamespace.Xmlns+"a",A),new XAttribute(XNamespace.Xmlns+"r",R),new XElement(P+"cSld",tree),new XElement(P+"clrMapOvr",new XElement(A+"masterClrMapping"))));
            Relationships(zip,$"ppt/slides/_rels/slide{index}.xml.rels",links);
        }
        Xml(zip,"ppt/presentation.xml",new XElement(P+"presentation",new XAttribute(XNamespace.Xmlns+"r",R),new XElement(P+"sldMasterIdLst",new XElement(P+"sldMasterId",new XAttribute("id",2147483648),new XAttribute(R+"id","master"))),ids,new XElement(P+"sldSz",new XAttribute("cx",(long)(width*12700)),new XAttribute("cy",(long)(height*12700))),new XElement(P+"notesSz",new XAttribute("cx",6858000),new XAttribute("cy",9144000))));
        Relationships(zip,"ppt/_rels/presentation.xml.rels",relationships);
        Xml(zip,"ppt/slideMasters/slideMaster1.xml",new XElement(P+"sldMaster",new XAttribute(XNamespace.Xmlns+"r",R),new XElement(P+"cSld",ShapeTree()),new XElement(P+"clrMap",new XAttribute("bg1","lt1"),new XAttribute("tx1","dk1"),new XAttribute("bg2","lt2"),new XAttribute("tx2","dk2"),Enumerable.Range(1,6).Select(i=>new XAttribute("accent"+i,"accent"+i)),new XAttribute("hlink","hlink"),new XAttribute("folHlink","folHlink")),new XElement(P+"sldLayoutIdLst",new XElement(P+"sldLayoutId",new XAttribute("id",2147483649),new XAttribute(R+"id","layout")))));
        Relationships(zip,"ppt/slideMasters/_rels/slideMaster1.xml.rels",[("layout",Rel("slideLayout"),"../slideLayouts/slideLayout1.xml")]);
        Xml(zip,"ppt/slideLayouts/slideLayout1.xml",new XElement(P+"sldLayout",new XAttribute("type","blank"),new XAttribute("preserve",1),new XElement(P+"cSld",ShapeTree()),new XElement(P+"clrMapOvr",new XElement(A+"masterClrMapping"))));
        Relationships(zip,"ppt/slideLayouts/_rels/slideLayout1.xml.rels",[("master",Rel("slideMaster"),"../slideMasters/slideMaster1.xml")]);
        Relationships(zip,"_rels/.rels",[("officeDocument",Rel("officeDocument"),"ppt/presentation.xml")]);
        XNamespace content="http://schemas.openxmlformats.org/package/2006/content-types";
        Xml(zip,"[Content_Types].xml",new XElement(content+"Types",new XElement(content+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(content+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(content+"Default",new XAttribute("Extension","png"),new XAttribute("ContentType","image/png")),types.Select(t=>new XElement(content+"Override",new XAttribute("PartName",t.Item1),new XAttribute("ContentType",t.Item2)))));
    }
    private static XElement ShapeTree()=>new(P+"spTree",new XElement(P+"nvGrpSpPr",new XElement(P+"cNvPr",new XAttribute("id",1),new XAttribute("name","")),new XElement(P+"cNvGrpSpPr"),new XElement(P+"nvPr")),new XElement(P+"grpSpPr",new XElement(A+"xfrm",new XElement(A+"off",new XAttribute("x",0),new XAttribute("y",0)),new XElement(A+"ext",new XAttribute("cx",0),new XAttribute("cy",0)),new XElement(A+"chOff",new XAttribute("x",0),new XAttribute("y",0)),new XElement(A+"chExt",new XAttribute("cx",0),new XAttribute("cy",0)))));
    private static XElement Transform(Citrus.Core.Editing.RectD b,double sw,double sh,double dw,double dh)=>new(A+"xfrm",new XElement(A+"off",new XAttribute("x",(long)(Math.Max(0,b.X)*dw/sw*12700)),new XAttribute("y",(long)(Math.Max(0,b.Y)*dh/sh*12700))),new XElement(A+"ext",new XAttribute("cx",(long)(Math.Max(1,b.Width)*dw/sw*12700)),new XAttribute("cy",(long)(Math.Max(1,b.Height)*dh/sh*12700))));
    private static string Rel(string type)=>"http://schemas.openxmlformats.org/officeDocument/2006/relationships/"+type;
    private static void Relationships(ZipArchive zip,string name,IEnumerable<(string Id,string Type,string Target)> relationships)
    { XNamespace ns="http://schemas.openxmlformats.org/package/2006/relationships"; Xml(zip,name,new XElement(ns+"Relationships",relationships.Select(r=>new XElement(ns+"Relationship",new XAttribute("Id",r.Id),new XAttribute("Type",r.Type),new XAttribute("Target",r.Target))))); }
    private static void Xml(ZipArchive zip,string name,XElement element) { using var sink=zip.CreateEntry(name).Open(); new XDocument(new XDeclaration("1.0","utf-8",null),element).Save(sink); }

    public static async Task WriteMarkdownBundleAsync(PdfSemanticDocument doc,string directory,CancellationToken ct)
    {
        Directory.CreateDirectory(directory);int image=0;
        foreach(var page in doc.Pages)foreach(var artwork in page.Images)await File.WriteAllBytesAsync(Path.Combine(directory,$"image-{++image}.png"),artwork.Png,ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory,"document.md"),Markdown(doc),new UTF8Encoding(false),ct).ConfigureAwait(false);
    }
    private static string Markdown(PdfSemanticDocument doc)
    {
        int image=0;var text=new StringBuilder(); foreach(var page in doc.Pages)
        {
            foreach(var line in page.Text) { if(line.FontSize>=18) text.Append("## "); text.AppendLine(line.Text).AppendLine(); }
            foreach(var table in page.Tables) for(int i=0;i<table.Rows.Length;i++) { text.Append('|').Append(string.Join('|',table.Rows[i].Select(c=>c.Replace("|","\\|",StringComparison.Ordinal).Replace("\n"," ",StringComparison.Ordinal)))).AppendLine("|"); if(i==0) text.Append('|').Append(string.Join('|',table.Rows[i].Select(_=>"---"))).AppendLine("|"); }
            foreach(var _ in page.Images)text.AppendLine($"![Recovered PDF artwork](image-{++image}.png)").AppendLine();
        } return text.ToString();
    }
    private static void Epub(PdfSemanticDocument document,string path,CancellationToken ct)
    {
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        using(var sink=new StreamWriter(zip.CreateEntry("mimetype",CompressionLevel.NoCompression).Open(),new UTF8Encoding(false))) sink.Write("application/epub+zip");
        XNamespace container="urn:oasis:names:tc:opendocument:xmlns:container",opf="http://www.idpf.org/2007/opf",dc="http://purl.org/dc/elements/1.1/",html="http://www.w3.org/1999/xhtml",epub="http://www.idpf.org/2007/ops";
        Xml(zip,"META-INF/container.xml",new XElement(container+"container",new XAttribute("version","1.0"),new XElement(container+"rootfiles",new XElement(container+"rootfile",new XAttribute("full-path","OEBPS/content.opf"),new XAttribute("media-type","application/oebps-package+xml")))));
        var manifest=new XElement(opf+"manifest",new XElement(opf+"item",new XAttribute("id","nav"),new XAttribute("href","nav.xhtml"),new XAttribute("media-type","application/xhtml+xml"),new XAttribute("properties","nav")));
        var spine=new XElement(opf+"spine"); var nav=new XElement(html+"ol"); int index=0;
        foreach(var page in document.Pages)
        {
            ct.ThrowIfCancellationRequested(); index++; string name=$"page{index}.xhtml"; manifest.Add(new XElement(opf+"item",new XAttribute("id","p"+index),new XAttribute("href",name),new XAttribute("media-type","application/xhtml+xml"))); spine.Add(new XElement(opf+"itemref",new XAttribute("idref","p"+index))); nav.Add(new XElement(html+"li",new XElement(html+"a",new XAttribute("href",name),"Page "+page.Number)));
            var body=new XElement(html+"body",page.Text.Select(t=>new XElement(html+(t.FontSize>=18?"h2":"p"),t.Text)),page.Tables.Select(t=>new XElement(html+"table",t.Rows.Select(row=>new XElement(html+"tr",row.Select(cell=>new XElement(html+"td",cell)))))));
            for(int i=0;i<page.Images.Length;i++) { string image=$"p{index}-image{i+1}.png"; manifest.Add(new XElement(opf+"item",new XAttribute("id",$"p{index}i{i+1}"),new XAttribute("href",image),new XAttribute("media-type","image/png"))); using(var sink=zip.CreateEntry("OEBPS/"+image).Open()) sink.Write(page.Images[i].Png); body.Add(new XElement(html+"img",new XAttribute("src",image),new XAttribute("alt","Recovered PDF artwork"))); }
            Xml(zip,"OEBPS/"+name,new XElement(html+"html",new XAttribute(XNamespace.Xml+"lang","en"),new XElement(html+"head",new XElement(html+"title","Page "+page.Number)),body));
        }
        Xml(zip,"OEBPS/nav.xhtml",new XElement(html+"html",new XAttribute(XNamespace.Xmlns+"epub",epub),new XElement(html+"head",new XElement(html+"title","Contents")),new XElement(html+"body",new XElement(html+"nav",new XAttribute(epub+"type","toc"),nav))));
        Xml(zip,"OEBPS/content.opf",new XElement(opf+"package",new XAttribute("version","3.0"),new XAttribute("unique-identifier","book-id"),new XElement(opf+"metadata",new XElement(dc+"identifier",new XAttribute("id","book-id"),"urn:uuid:"+Guid.NewGuid()),new XElement(dc+"title","Converted PDF"),new XElement(dc+"language","en"),new XElement(opf+"meta",new XAttribute("property","dcterms:modified"),DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture))),manifest,spine));
    }
}
