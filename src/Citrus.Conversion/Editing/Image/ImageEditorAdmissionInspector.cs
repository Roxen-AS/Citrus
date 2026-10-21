using System.Collections.Immutable;
using Citrus.Core;
using Citrus.Core.Editing;
using ImageMagick;

namespace Citrus.Conversion.Editing.Image;

/// <summary>Admission inspects verified local copies; an extension alone never admits a raster.</summary>
public sealed class ImageEditorAdmissionInspector(ISourceSnapshotService sources) : IEditorAdmissionInspector
{
    public Task<ImmutableArray<ValidationIssue>> ValidateAsync(OperationRequest request, CancellationToken ct) => Task.Run(async () =>
    {
        ct.ThrowIfCancellationRequested(); string scope = Path.Combine(Path.GetTempPath(), "citrus-image-inspect-" + Guid.NewGuid().ToString("N"));
        try
        {
            var document = request.Editor ?? throw new ArgumentException("A typed image document is required.");
            var recipe = ImageRecipeRenderer.Validate(document); EditorExportPolicy.Resolve(request);
            if (!ImageEditorEngine.AcceptsDescriptor(request.Operation)) throw new NotSupportedException("This image tool is not implemented yet.");
            ImageRasterSource.CheckAvailable(document.Sources);
            var staged = await sources.StageVerifiedAsync(document.Sources, scope, ct).ConfigureAwait(false);
            var fonts = await new FontArtifactService().StageVerifiedAsync(document.Fonts, scope, ct).ConfigureAwait(false);
            FontTextValidator.ValidateDocument(document, fonts, ct);
            foreach (var auxiliary in document.Sources.Where(s => s.Id != recipe.SourceId)) ImageRasterSource.Inspect(staged[auxiliary.Id], ct);
            using var image = ImageRasterSource.Load(staged[recipe.SourceId], ct);
            foreach (var mask in recipe.Masks)
                if (mask.Bounds.X < 0 || mask.Bounds.Y < 0 || mask.Bounds.X + mask.Bounds.Width > image.Width || mask.Bounds.Y + mask.Bounds.Height > image.Height) throw new ArgumentException("Mask must fit normalized source-plane S.");
            ImageGeometryMapper.Resolve(recipe, new((int)image.Width, (int)image.Height));
            ct.ThrowIfCancellationRequested(); return ImmutableArray<ValidationIssue>.Empty;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or InvalidDataException or UnauthorizedAccessException or MagickException)
        { return [new("Image", error.Message)]; }
        finally { if (Directory.Exists(scope)) Directory.Delete(scope, true); }
    }, ct);
}
