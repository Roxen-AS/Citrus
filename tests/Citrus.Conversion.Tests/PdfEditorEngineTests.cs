using System.Collections.Immutable;
using Citrus.Conversion.Editing;
using Citrus.Core;
using Citrus.Core.Editing;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Pig = UglyToad.PdfPig.PdfDocument;

namespace Citrus.Conversion.Tests;

public sealed class PdfEditorEngineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "citrus-pdf-editor-" + Guid.NewGuid().ToString("N"));
    public PdfEditorEngineTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string Input()
    {
        var builder = new PdfDocumentBuilder(); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (string text in new[] { "First", "Second", "Third" }) builder.AddPage(144, 72).AddText(text, 12, new PdfPoint(5, 30), font);
        string path = Path.Combine(root, "pages.pdf"); File.WriteAllBytes(path, builder.Build()); return path;
    }
    private async Task<OperationRequest> Request(string id, string path, params int[] pages)
    {
        var snapshot = (await new SourceSnapshotService().CaptureAsync([path], default)).Single().Snapshot!;
        var recipe = new PdfRecipe([.. pages.Select((p,i) => new PdfPageRef(Guid.NewGuid(),snapshot.Id,p,i == 0 ? 1 : 0))], PdfOutputMode.Document,150,PdfCompressionMode.PreserveStructure,85,[]);
        var document = new EditorDocument(1,0,[snapshot],[],recipe,new("pdf",root,"organized",new QualityGoal(85),MetadataPolicy.PreserveSupported,new(255,255,255,255)));
        return new(new PdfEngine().Operations.Single(operation=>operation.Id==id),[path],new(),root,document);
    }
    [Fact]
    public async Task Typed_organizer_preserves_order_duplicates_rotation_and_source_bytes()
    {
        string path = Input(); byte[] original = File.ReadAllBytes(path); var engine = new PdfEngine();
        var request = await Request("pdf.editor",path,3,1,3);
        Assert.True(engine.CanExecute(request));
        string output = Assert.Single((await engine.ExecuteAsync(request,null,default)).Outputs);
        using var pdf = Pig.Open(output);
        Assert.Equal(new[] { "Third","First","Third" },pdf.GetPages().Select(p => p.Text));
        Assert.Equal(90,(int)pdf.GetPage(1).Rotation.Value); Assert.Equal(original,File.ReadAllBytes(path));
    }
    [Fact]
    public async Task Typed_editor_rejects_mutated_source_before_publication()
    {
        string path = Input(); var request = await Request("pdf.editor",path,1); File.AppendAllText(path,"mutation");
        await Assert.ThrowsAsync<IOException>(() => new PdfEngine().ExecuteAsync(request,null,default));
        Assert.Equal(new[] { path },Directory.GetFiles(root));
    }
    [Fact]
    public async Task Typed_editor_removes_only_its_published_copy_when_final_notification_fails()
    {
        string path=Input();byte[] original=File.ReadAllBytes(path);var request=await Request("pdf.editor",path,1);
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>new PdfEngine().ExecuteAsync(request,new FinalFailure(),default));
        Assert.Equal("Final notification failed",failure.Message);Assert.Equal(new[]{path},Directory.GetFileSystemEntries(root));Assert.Equal(original,File.ReadAllBytes(path));
    }
    [Fact]
    public async Task Typed_split_reopens_every_selected_page_as_an_independent_document()
    {
        string path=Input();byte[] original=File.ReadAllBytes(path);var request=await Request("pdf.split",path,1,3);
        request=request with {Editor=request.Editor! with {Recipe=((PdfRecipe)request.Editor!.Recipe) with {Mode=PdfOutputMode.SeparatePageDocuments}}};
        var result=await new PdfEngine().ExecuteAsync(request,null,default);string directory=Assert.Single(result.Outputs);var text=new List<string>();
        foreach(string member in Directory.GetFiles(directory).Order()){using var pdf=Pig.Open(member);Assert.Equal(1,pdf.NumberOfPages);text.Add(pdf.GetPage(1).Text);}
        Assert.Equal(new[]{"First","Third"},text);Assert.Equal(original,File.ReadAllBytes(path));Assert.Equal(Directory.GetFiles(directory).Sum(member=>new FileInfo(member).Length),result.Compression!.ActualBytes);
    }
    [Fact]
    public async Task Unreachable_compression_byte_goal_keeps_original_and_publishes_nothing()
    {
        string path=Input();byte[] original=File.ReadAllBytes(path);var request=await Request("pdf.compress",path,1,2,3);
        request=request with {Editor=request.Editor! with {Export=request.Editor!.Export with {Compression=new ByteGoal(3)}}};
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>new PdfEngine().ExecuteAsync(request,null,default));Assert.Contains("Best measured size",error.Message);
        Assert.Equal(new[]{path},Directory.GetFileSystemEntries(root));Assert.Equal(original,File.ReadAllBytes(path));
    }
    private sealed class FinalFailure:IProgress<ConversionProgress>
    {public void Report(ConversionProgress progress){if(progress.Message=="PDF copy ready")throw new InvalidOperationException("Final notification failed");}}
}
