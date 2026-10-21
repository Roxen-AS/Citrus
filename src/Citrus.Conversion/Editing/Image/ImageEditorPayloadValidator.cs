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
        SizeI sourceSize = ImageRasterSource.Inspect(original, ct); var (_, photoSize) = ImageGeometryMapper.Resolve(recipe, sourceSize); var expected=recipe.Canvas?.Size??photoSize;
        var member = staged.Files[0];
        if (!EditorPathPolicy.IsLeaf(member.RelativePath) || Path.GetExtension(member.RelativePath) != "." + document.Export.FormatId) throw new InvalidDataException("Invalid output filename.");
        string path = Path.Combine(privateDirectory, "payload", member.RelativePath); SafeEditorFiles.RejectLinks(path);
        if (new FileInfo(path).Length != member.Bytes) throw new InvalidDataException("Output length changed.");
        if(staged.Compression.TargetBytes is {} target && member.Bytes>target)throw new InvalidDataException("Encoded output exceeds its requested byte ceiling.");
        SizeI actual = ImageRasterSource.Inspect(path, ct);
        if (actual != expected) throw new InvalidDataException("Encoded image dimensions do not match the recipe.");
        // Decode raw, without AutoOrient/profile normalization: reject an incorrectly encoded orientation.
        using var image = ImageRasterSource.LoadEncoded(path,document.Export.FormatId,ct);
        if (image.Width != expected.Width || image.Height != expected.Height || image.Orientation is not (Orientation.Undefined or Orientation.TopLeft)) throw new InvalidDataException("Output pixels are not orientation-normalized.");
        if (document.Export.FormatId is "jpg" or "jpeg" or "bmp" or "gif" && image.HasAlpha) throw new InvalidDataException("Opaque output unexpectedly retained alpha.");
        ValidateMetadata(image, document.Export.Metadata);
        if (recipe.Masks.Any(m => m.Effect == MaskEffect.Solid))
            ValidateMetadata(image, document.Export.Metadata == MetadataPolicy.StripAll ? MetadataPolicy.StripAll : MetadataPolicy.StripDescriptive);
        // Force access to decoded pixels; Ping alone is insufficient for truncated/corrupt raster payloads.
        using var pixels = image.GetPixels(); _ = pixels.GetPixel(0, 0);
        ct.ThrowIfCancellationRequested();
    }, ct);
    // These are decoder-derived raster facts, not arbitrary metadata namespaces. In particular,
    // do not allow all png:/tiff:/exif: attributes: those namespaces can also contain user text.
    private static readonly HashSet<string> TechnicalAttributes = new(StringComparer.Ordinal)
    {
        "date:create", "date:modify", "date:timestamp",
        "png:IHDR.bit-depth-orig", "png:IHDR.bit_depth", "png:IHDR.color-type-orig",
        "png:IHDR.color_type", "png:IHDR.interlace_method", "png:IHDR.width,height",
        "png:PLTE.number_colors", "png:bKGD", "png:cHRM", "png:gAMA", "png:iCCP",
        "png:sRGB", "png:pHYs", "png:tRNS", "png:text", "png:text-encoded profiles",
        "jpeg:colorspace", "jpeg:sampling-factor",
        "tiff:alpha", "tiff:endian", "tiff:photometric", "tiff:rows-per-strip"
    };
    private static readonly IReadOnlyDictionary<string, string> WorkingProfileAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["icc:copyright"] = "Copyright (c) 1998 Hewlett-Packard Company",
        ["icc:description"] = "sRGB IEC61966-2.1",
        ["icc:manufacturer"] = "IEC http://www.iec.ch",
        ["icc:model"] = "IEC 61966-2.1 Default RGB colour space - sRGB"
    };
    private static void ValidateMetadata(MagickImage image, MetadataPolicy policy)
    {
        var profile = image.GetColorProfile();
        if (policy != MetadataPolicy.StripAll)
        {
            // Match the actual canonical working profile, not a descriptive name saying "sRGB".
            if (profile is null || !profile.ToByteArray().AsSpan().SequenceEqual(ColorProfiles.SRGB.ToByteArray()))
                throw new InvalidDataException("Output must retain the canonical sRGB working profile.");
        }
        if (image.ColorSpace is not (ColorSpace.sRGB or ColorSpace.Gray))
            throw new InvalidDataException("Output pixels must use the normalized SDR sRGB working space.");
        if (policy == MetadataPolicy.PreserveSupported) return;
        foreach (string name in image.ProfileNames)
            if (policy == MetadataPolicy.StripAll || !name.Equals("icc", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Stripped output retained the {name} profile.");
        foreach (string name in image.AttributeNames)
        {
            if (TechnicalAttributes.Contains(name)) continue;
            if (policy == MetadataPolicy.StripDescriptive && WorkingProfileAttributes.TryGetValue(name, out string? value) && image.GetAttribute(name) == value) continue;
            throw new InvalidDataException($"Stripped output retained the {name} metadata attribute.");
        }
    }

}
