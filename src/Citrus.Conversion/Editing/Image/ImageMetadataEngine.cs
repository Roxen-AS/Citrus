using System.Collections.Immutable;
using Citrus.Core;
using Citrus.Core.Editing;
using ImageMagick;

namespace Citrus.Conversion.Editing.Image;

/// <summary>Metadata-only copies preserve encoded image payloads instead of passing through a raster encoder.</summary>
public sealed class ImageMetadataEngine(OutputWriter writer,ExifToolMetadataService metadata,ISourceSnapshotService sources):IConversionEngine,IEditorAdmissionInspector
{
    public ExifToolMetadataService Metadata=>metadata;
    public ISourceSnapshotService Sources=>sources;
    public EditorRouteRegistry Routes{get;}=new([new("image.metadata","Metadata",EditorSubmissionShape.PerRoot)]);
    private static readonly OperationDescriptor Descriptor=new("image.metadata","Metadata",OperationMode.Tools,null,false,true);
    public IReadOnlyList<OperationDescriptor> Operations{get;}=Array.AsReadOnly(new[]{Descriptor});
    public bool CanExecute(OperationRequest request)=>request.Operation==Descriptor&&metadata.Available&&request.Inputs.Count==1&&ImageRasterSource.Supports(request.Inputs[0])&&DropValidation.Accepts(request.Inputs,OperationMode.Tools);
    public async Task<ImmutableArray<ValidationIssue>> ValidateAsync(OperationRequest request,CancellationToken ct)
    {
        try
        {
            var recipe=Validate(request);var fields=await metadata.ReadAsync(request.Editor!.Sources.Single(s=>s.Id==recipe.SourceId),ct).ConfigureAwait(false);
            return metadata.ValidateEdits(fields,recipe.MetadataEdits);
        }
        catch(Exception e)when(e is ArgumentException or IOException or NotSupportedException){return[new("Metadata",e.Message)];}
    }
    private ImageRecipe Validate(OperationRequest request)
    {
        if(!CanExecute(request)||request.Editor is not {} document||document.Recipe is not ImageRecipe recipe)throw new ArgumentException("A captured metadata document is required.");
        var issues=EditorRecipeValidator.Validate(document);if(!issues.IsEmpty)throw new ArgumentException(string.Join(" ",issues.Select(i=>i.Message)));EditorExportPolicy.Resolve(request);
        if(recipe.Geometry.QuarterTurns!=0||recipe.Geometry.FlipX||recipe.Geometry.FlipY||recipe.Geometry.Crop is not null||recipe.Geometry.OutputSize is not null||recipe.Canvas is not null||!recipe.Masks.IsEmpty||!recipe.Annotations.IsEmpty)throw new ArgumentException("Metadata-only copies cannot contain pixel edits.");
        string original=Path.GetExtension(document.Sources.Single(s=>s.Id==recipe.SourceId).Path)[1..].ToLowerInvariant();if(document.Export.FormatId!=original)throw new ArgumentException("Metadata copies keep their original format.");
        var identity=new ImageAdjustments(1,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,recipe.Adjustments.GrainSeed,PhotoEffect.None,0);
        if(recipe.Adjustments with{EffectAmount=0}!=identity)throw new ArgumentException("Metadata-only copies cannot contain photo adjustments.");return recipe;
    }
    public async Task<ConversionResult> ExecuteAsync(OperationRequest request,IProgress<ConversionProgress>? progress,CancellationToken ct)
    {
        var recipe=Validate(request);var doc=request.Editor!;var source=doc.Sources.Single(s=>s.Id==recipe.SourceId);string scope=Path.Combine(Path.GetTempPath(),"citrus-metadata-save-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=await sources.StageVerifiedAsync([source],scope,ct).ConfigureAwait(false);string staged=paths[source.Id];
            var fields=await metadata.ReadPathAsync(staged,ct).ConfigureAwait(false);var issues=metadata.ValidateEdits(fields,recipe.MetadataEdits);if(!issues.IsEmpty)throw new ArgumentException(string.Join(" ",issues.Select(i=>i.Message)));
            var target=EditorExportPolicy.Resolve(request);string desired=Path.Combine(target.Directory,target.Stem+"-metadata"+target.Extension);
            string output=await writer.WriteFileAsync(desired,async(path,token)=>
            {
                progress?.Report(new(null,"Saving verified metadata copy"));File.Copy(staged,path,false);
                await metadata.ApplyAsync(path,recipe.MetadataEdits,token,doc.Export.Metadata!=MetadataPolicy.PreserveSupported).ConfigureAwait(false);
                using var before=ImageRasterSource.LoadEncoded(staged,doc.Export.FormatId,token);using var after=ImageRasterSource.LoadEncoded(path,doc.Export.FormatId,token);
                if(before.Width!=after.Width||before.Height!=after.Height)throw new InvalidDataException("Metadata changes altered image dimensions.");
                using var beforePixels=before.GetPixels();using var afterPixels=after.GetPixels();
                for(int y=0;y<before.Height;y++){token.ThrowIfCancellationRequested();if(!beforePixels.ToByteArray(0,y,before.Width,1,PixelMapping.RGBA)!.AsSpan().SequenceEqual(afterPixels.ToByteArray(0,y,after.Width,1,PixelMapping.RGBA)))throw new InvalidDataException("Metadata changes altered decoded image pixels.");}
            },ct).ConfigureAwait(false);
            return new([output]);
        }
        finally{if(Directory.Exists(scope))Directory.Delete(scope,true);}
    }
}
