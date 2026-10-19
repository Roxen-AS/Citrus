namespace Citrus.Core.Editing;

/// <summary>Shared trusted family/mode policy; renderers never choose destinations or suffixes.</summary>
public static class EditorExportPolicy
{
    private static readonly HashSet<string> Raster = new(StringComparer.Ordinal) { "jpg","jpeg","png","webp","tif","tiff","bmp","gif","avif","heic","ico" };
    public static ExportTarget Resolve(OperationRequest request)
    {
        var doc = request.Editor ?? throw new ArgumentException("An editor document is required.");
        if (EditorRecipeValidator.Validate(doc).Length > 0) throw new ArgumentException("Invalid editor document.");
        var format = doc.Export.FormatId;
        string id = request.Operation.Id;
        bool allowed;
        ExportShape shape = ExportShape.File;
        switch (doc.Recipe)
        {
            case ImageRecipe:
                allowed = id is "image.resize" or "image.rotate" or "image.compress" or "image.editor" or "image.crop" or "image.annotate" or "image.background" or "image.redact" or "image.metadata";
                allowed &= Raster.Contains(format); break;
            case CollageRecipe: allowed = id == "image.collage" && Raster.Contains(format); break;
            case VideoRecipe video:
                allowed = id is "video.trim" or "video.resize" or "video.speed" or "video.editor" or "video.crop" or "video.mute" or "video.snapshot" or "video.split" or "video.redact" or "video.compress" or "video.metadata" or "video.join";
                allowed &= video.SnapshotAt is not null ? format is "png" or "jpg" or "jpeg" or "webp" : format == "mp4";
                if (!video.SplitAt.IsEmpty) shape = ExportShape.Directory;
                if (id == "video.snapshot") allowed &= video.SnapshotAt is not null;
                if (id == "video.split") allowed &= !video.SplitAt.IsEmpty;
                break;
            case AudioRecipe:
                allowed = id is "audio.trim" or "audio.normalize" or "audio.editor" or "audio.channels" or "audio.bleep" or "audio.compress" or "audio.metadata" or "audio.join";
                allowed &= id is "audio.compress" or "audio.metadata" ? format is "wav" or "mp3" or "m4a" or "aac" or "flac" or "ogg" : format == "wav"; break;
            case VisualizerRecipe visualizer:
                allowed = id == "audio.visualizer" && (visualizer.Mode == VisualizerMode.StillWaveform ? format is "png" or "jpg" or "jpeg" or "webp" : format == "mp4"); break;
            case PdfRecipe pdf:
                allowed = id is "pdf.merge" or "pdf.organize" or "pdf.editor" or "pdf.compress" or "pdf.metadata" or "pdf.split" or "pdf.images" or "pdf.text";
                allowed &= pdf.Mode switch { PdfOutputMode.Document or PdfOutputMode.SeparatePageDocuments => format == "pdf", PdfOutputMode.PageImages => format is "png" or "jpg" or "jpeg" or "webp" or "tif" or "tiff", PdfOutputMode.Text => format == "txt", PdfOutputMode.Markdown => format == "md", _ => false };
                if (pdf.Mode is PdfOutputMode.PageImages or PdfOutputMode.SeparatePageDocuments) shape = ExportShape.Directory;
                if (id == "pdf.split") allowed &= pdf.Mode == PdfOutputMode.SeparatePageDocuments;
                if (id == "pdf.images") allowed &= pdf.Mode == PdfOutputMode.PageImages;
                if (id == "pdf.text") allowed &= pdf.Mode == PdfOutputMode.Text;
                break;
            default: allowed = false; break;
        }
        if (!allowed) throw new ArgumentException("Operation, recipe mode and export format are incompatible.");
        bool group = id is "image.collage" or "video.join" or "audio.join" or "pdf.merge" or "pdf.organize";
        if (request.Operation.IsGroup != group || request.Inputs.Count == 0 || (!group && request.Inputs.Count != 1)) throw new ArgumentException("Operation root shape mismatch.");
        var roots = EditorRecipeReferences.Roots(doc.Recipe);
        if (roots.Length != request.Inputs.Count || request.Inputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Inputs.Count || request.Inputs.Any(path => !Path.IsPathFullyQualified(path) || !doc.Sources.Any(s => roots.Contains(s.Id) && EditorPathPolicy.Same(s.Path,path))))
            throw new ArgumentException("Input paths do not identify the recipe roots.");
        string? chosen = doc.Export.DestinationDirectory ?? request.DestinationDirectory;
        if (doc.Export.DestinationDirectory is not null && request.DestinationDirectory is not null && !EditorPathPolicy.Same(doc.Export.DestinationDirectory,request.DestinationDirectory)) throw new ArgumentException("Conflicting destinations.");
        var directories = request.Inputs.Select(p => Path.GetDirectoryName(Path.GetFullPath(p))!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (chosen is null && directories.Length != 1) throw new ArgumentException("Grouped roots in different folders require a destination.");
        string destination = chosen ?? directories[0];
        if (!Path.IsPathFullyQualified(destination)) throw new ArgumentException("Destination must be absolute.");
        return new(Path.GetFullPath(destination),doc.Export.OutputStem,shape == ExportShape.File ? "." + format : null,shape);
    }
}
