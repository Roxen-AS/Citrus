using Citrus.Core;
using Citrus.Core.Editing;
using ImageMagick;

namespace Citrus.Conversion.Editing.Image;

/// <summary>Independent explicit-format decode, dimensions and normalized-output checks before publication.</summary>
public sealed class ImageEditorPayloadValidator : IEditorPayloadValidator
{
    public Task ValidateAsync(OperationRequest request, StagedExport staged, string privateDirectory, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested(); var document = request.Editor ?? throw new ArgumentException("Typed image document required.");
        var recipe = ImageRecipeRenderer.Validate(document); EditorExportPolicy.Resolve(request);
        if (staged.Shape != ExportShape.File || staged.Files.Length != 1) throw new InvalidDataException("Image output must contain one file.");
        var source = document.Sources.Single(s => s.Id == recipe.SourceId);
        string original = Path.Combine(privateDirectory, "sources", source.Id.ToString("N") + Path.GetExtension(source.Path)); SafeEditorFiles.RejectLinks(original);
        SizeI sourceSize = ImageRasterSource.Inspect(original, ct); var (_, expected) = ImageGeometryMapper.Resolve(recipe, sourceSize);
        var member = staged.Files[0];
        if (!EditorPathPolicy.IsLeaf(member.RelativePath) || Path.GetExtension(member.RelativePath) != "." + document.Export.FormatId) throw new InvalidDataException("Invalid output filename.");
        string path = Path.Combine(privateDirectory, "payload", member.RelativePath); SafeEditorFiles.RejectLinks(path);
        if (new FileInfo(path).Length != member.Bytes) throw new InvalidDataException("Output length changed.");
        SizeI actual = ImageRasterSource.Inspect(path, ct);
        if (actual != expected) throw new InvalidDataException("Encoded image dimensions do not match the recipe.");
        // Decode raw, without AutoOrient/profile normalization: reject an incorrectly encoded orientation.
        using var image = new MagickImage(path, new MagickReadSettings { Format = ImageRasterSource.Format(document.Export.FormatId) });
        if (image.Width != expected.Width || image.Height != expected.Height || image.Orientation is not (Orientation.Undefined or Orientation.TopLeft)) throw new InvalidDataException("Output pixels are not orientation-normalized.");
        if (document.Export.FormatId is "jpg" or "jpeg" or "bmp" or "gif" && image.HasAlpha) throw new InvalidDataException("Opaque output unexpectedly retained alpha.");
        if (document.Export.Metadata == MetadataPolicy.StripAll && image.ProfileNames.Any()) throw new InvalidDataException("StripAll output retained a profile.");
        // Force access to decoded pixels; Ping alone is insufficient for truncated/corrupt raster payloads.
        using var pixels = image.GetPixels(); _ = pixels.GetPixel(0, 0);
        ct.ThrowIfCancellationRequested();
    }, ct);
}
