using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Image;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Windows.Editors;
using Citrus.Windows.Editors.Image;
using ImageMagick;

namespace Citrus.Conversion.Tests;

public sealed class AnnotationHostBoundaryTests
{
    [Fact]
    public async Task Inset_import_stays_auxiliary_and_actual_export_preserves_root_identity()
    {
        using var f=new ImageLayerFixture();var initial=await f.Create(80,60);var aux=await f.Create(10,10);
        using var services=new ImageEditorServices(new());var request=Request("image.annotate","Annotate",initial.Sources[0].Path);
        var model=new EditorHostViewModel(request,services);await model.CaptureAsync(request.InitialPaths,false,default);
        var source=await model.ImportAuxiliaryAsync(aux.Sources[0].Path,default);
        Assert.Single(model.Sources);Assert.Single(model.Selected!.Draft!.Sources);Assert.False(model.Selected.Dirty);
        var doc=model.Selected.Draft;var image=(ImageRecipe)doc.Recipe;
        model.Change(doc with {Sources=doc.Sources.Add(source),Recipe=image with {Annotations=[new(Guid.NewGuid(),true,new InsetImageGeometry(source.Id,new(10,10,20,20)),ImageLayerFixture.Red)]}});
        var batch=model.Accept();Assert.Equal(request.InitialPaths.Single(),batch.InitialSources.Single().Path);Assert.Equal(EditorImportRole.Auxiliary,batch.Imports.Single().Role);Assert.Single(batch.Items[0].RootSourceIds);
        var queue=new JobQueue([services.Engine]);var job=Assert.Single(await new EditorSubmissionService(queue,services.Routes,services.Inspector).SubmitAsync(batch,default));await job.Completion;
        Assert.Equal(ConversionJobState.Succeeded,job.State);using var stream=File.OpenRead(job.Outputs.Single());using var encoded=new MagickImage(stream,new MagickReadSettings{Format=MagickFormat.Png});Assert.Equal(80u,encoded.Width);Assert.Equal(60u,encoded.Height);
    }
    [Fact]
    public async Task Root_cannot_be_silently_imported_as_an_auxiliary()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();using var services=new ImageEditorServices(new());var request=Request("image.annotate","Annotate",doc.Sources[0].Path);var model=new EditorHostViewModel(request,services);await model.CaptureAsync(request.InitialPaths,false,default);
        await Assert.ThrowsAsync<ArgumentException>(()=>model.ImportAuxiliaryAsync(request.InitialPaths[0],default));Assert.Single(model.Sources);Assert.Single(model.Accept().Items);
    }
    [Fact]
    public async Task Single_gesture_history_restores_move_reorder_delete_and_stable_numbers()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();using var services=new ImageEditorServices(new());var request=Request("image.annotate","Annotate",doc.Sources[0].Path);var model=new EditorHostViewModel(request,services);await model.CaptureAsync(request.InitialPaths,false,default);
        var initial=model.Selected!.Draft!;var font=AnnotationRenderingTests.Font();var style=ImageLayerFixture.Red with {Font=new(font.Id,12,15)};
        var a=new Annotation(Guid.NewGuid(),true,new NumberGeometry(new(10,10,20,20),2),style);var b=a with {Id=Guid.NewGuid(),Geometry=new NumberGeometry(new(40,10,20,20),7)};
        model.Change(initial with {Fonts=[font],Recipe=((ImageRecipe)initial.Recipe) with {Annotations=[a,b]}});var before=model.Selected.Draft!;
        for(int i=1;i<=20;i++)model.Change(before with {Recipe=((ImageRecipe)before.Recipe) with {Annotations=[ImageLayerGeometry.Move(a,i,0),b]}},true);
        model.CommitGesture();model.Undo();Assert.Equal(before.Recipe,model.Selected.Draft!.Recipe);model.Redo();Assert.Equal(30,((NumberGeometry)((ImageRecipe)model.Selected.Draft!.Recipe).Annotations[0].Geometry).Bounds.X);
        var moved=model.Selected.Draft!;model.Change(moved with {Recipe=((ImageRecipe)moved.Recipe) with {Annotations=[b]}});Assert.Equal(7,((NumberGeometry)((ImageRecipe)model.Selected.Draft!.Recipe).Annotations.Single().Geometry).Number);model.Undo();Assert.Equal(2,((ImageRecipe)model.Selected.Draft!.Recipe).Annotations.Length);
    }
    [Fact]
    public async Task Changed_font_is_visible_before_accept_and_explicit_resolution_restores_real_export()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();using var services=new ImageEditorServices(new());var request=Request("image.annotate","Annotate",doc.Sources[0].Path);var model=new EditorHostViewModel(request,services);await model.CaptureAsync(request.InitialPaths,false,default);
        string path=f.PathIn("chosen.ttf");File.Copy(AnnotationRenderingTests.Font().OriginalPath,path);var font=await services.Fonts.CaptureAsync(path,0,default);var initial=model.Selected!.Draft!;
        model.Change(initial with {Fonts=[font],Recipe=((ImageRecipe)initial.Recipe) with {Annotations=[new(Guid.NewGuid(),true,new TextGeometry(new(5,5),"AB",60,0),ImageLayerFixture.Red with {Font=new(font.Id,12,15)})]}});
        await model.ValidateLayerDependenciesAsync(default);File.Delete(path);await Assert.ThrowsAsync<ArgumentException>(()=>model.ValidateLayerDependenciesAsync(default));
        File.Copy(AnnotationRenderingTests.Font(2).OriginalPath,path);var resolved=await services.Fonts.CaptureAsync(path,0,default);var current=model.Selected.Draft!;var image=(ImageRecipe)current.Recipe;var layer=image.Annotations[0];model.Change(current with {Fonts=[resolved],Recipe=image with {Annotations=[layer with {Style=layer.Style with {Font=layer.Style.Font! with {ArtifactId=resolved.Id}}}]}});
        await model.ValidateLayerDependenciesAsync(default);Assert.NotEqual(font.Id,resolved.Id);
        var batch=model.Accept();var job=Assert.Single(await new EditorSubmissionService(new JobQueue([services.Engine]),services.Routes,services.Inspector).SubmitAsync(batch,default));await job.Completion;Assert.Equal(ConversionJobState.Succeeded,job.State);
    }
    [Fact]
    public async Task Manual_mask_route_changes_actual_pixels_and_keeps_original_bytes()
    {
        using var f=new ImageLayerFixture();var doc=await f.Create();string original=doc.Sources[0].Sha256;using var services=new ImageEditorServices(new());var request=Request("image.redact","Manual masks",doc.Sources[0].Path);var model=new EditorHostViewModel(request,services);await model.CaptureAsync(request.InitialPaths,false,default);
        var draft=model.Selected!.Draft!;model.Change(draft with {Recipe=((ImageRecipe)draft.Recipe) with {Masks=[new(Guid.NewGuid(),new(5,5,20,20),MaskEffect.Solid,new(0,0,0,255),0)]}});
        var batch=model.Accept();var job=Assert.Single(await new EditorSubmissionService(new JobQueue([services.Engine]),services.Routes,services.Inspector).SubmitAsync(batch,default));await job.Completion;Assert.Equal(ConversionJobState.Succeeded,job.State);
        Assert.Equal(new byte[]{0,0,0,255},ImageLayerFixture.Pixels(job.Outputs.Single()).Skip((10*80+10)*4).Take(4));Assert.Equal(original,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(request.InitialPaths[0]))));
    }
    [Fact]
    public async Task Text_hit_metrics_use_exact_selected_face_wrapping_and_rotation()
    {
        var artifact=AnnotationRenderingTests.Font();var font=new FontSpec(artifact.Id,12,15);var text=new TextGeometry(new(10,20),"AB中文",25,90);
        var box=ImageLayerTextMetrics.Measure(artifact,font,text);Assert.Equal(25,box.Width);Assert.True(box.Height>15);
        var a=new Annotation(Guid.NewGuid(),true,text,ImageLayerFixture.Red with {Font=font});var p=ImageLayerGeometry.Rotate(new(12,22),text.Origin,90);Assert.True(ImageLayerGeometry.Hit(a,p,0,box));
        await Task.CompletedTask;
    }
    [Fact]
    public async Task Actual_capability_check_covers_annotation_and_manual_mask_renderer_before_routes_are_available()
    {
        using var services=new ImageEditorServices(new());await services.VerifyAsync(default);
        Assert.True(services.Routes.TryGet("image.annotate",out var annotate));Assert.Equal("Annotate",annotate.EntryTool);
        Assert.True(services.Routes.TryGet("image.redact",out var redact));Assert.Equal("Manual masks",redact.EntryTool);
        Assert.False(services.Engine.CanExecute(new(services.Engine.Operations.Single(o=>o.Id=="image.annotate"),["fake.svg"],new(),null)));
    }
    private static EditorOpenRequest Request(string id,string tool,string path)=>new(new(id,tool,OperationMode.Tools,null,false,true),[path],tool,new(),null,SubmissionOrigin.Wheel);
}
