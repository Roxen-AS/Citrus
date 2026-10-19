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
            using var image = ImageRasterSource.Load(staged[recipe.SourceId], ct);
            ImageGeometryMapper.Resolve(recipe, new((int)image.Width, (int)image.Height));
            ct.ThrowIfCancellationRequested(); return ImmutableArray<ValidationIssue>.Empty;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or InvalidDataException or UnauthorizedAccessException or MagickException)
        { return [new("Image", error.Message)]; }
        finally { if (Directory.Exists(scope)) Directory.Delete(scope, true); }
    }, ct);
}
