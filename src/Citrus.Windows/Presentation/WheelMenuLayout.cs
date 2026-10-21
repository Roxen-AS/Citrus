using Citrus.Core;

namespace Citrus.Windows.Presentation;

internal static class WheelMenuLayout
{
    internal static IReadOnlyList<OperationDescriptor>[] Build(IReadOnlyList<OperationDescriptor> operations, OperationMode mode, IReadOnlyList<string> paths)
    {
        bool Archive(OperationDescriptor operation) => operation.Id.StartsWith("archive.", StringComparison.Ordinal);
        var available = operations.Where(operation => mode != OperationMode.Convert || Archive(operation) || paths.Count == 0 ||
            operation.OutputExtension is null || !paths.All(path => Canonical(Path.GetExtension(path)) == Canonical(operation.OutputExtension))).ToArray();
        // Context formats and archive containers have separate pages. A short first page stays
        // evenly spaced; blank targets never occupy slots merely to fill the maximum capacity.
        IEnumerable<OperationDescriptor>[] groups;
        if (mode == OperationMode.Convert)
        {
            var formats = available.Where(operation => !Archive(operation)).ToArray();
            var primary = ReferenceFormats(paths);
            groups = primary.Length == 0 ? [formats, available.Where(Archive)]
                : [primary.SelectMany(format => formats.Where(operation => Canonical(operation.OutputExtension ?? "") == format)),
                   formats.Where(operation => !primary.Contains(Canonical(operation.OutputExtension ?? ""), StringComparer.Ordinal)), available.Where(Archive)];
        }
        else
        {
            var primary = ReferenceTools(paths);
            groups = mode != OperationMode.Tools || primary.Length == 0 ? [available]
                : [primary.SelectMany(id => available.Where(operation => operation.Id == id)), available.Where(operation => !primary.Contains(operation.Id, StringComparer.Ordinal))];
        }
        var pages = groups.SelectMany(group => group.Chunk(WheelGeometry.MaxTargetsPerPage))
            .Select(chunk => (IReadOnlyList<OperationDescriptor>)chunk).ToList();
        // Small context families intentionally keep their actual 1/2/3/4-choice
        // geometry, as shown in the supplied archive/document references.
        return pages.ToArray();
    }
    private static string Canonical(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "jpeg", "tif" or "tiff" => "tiff", "aif" or "aiff" => "aiff",
        var value => value
    };
    private static string Family(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return "";
        string Kind(string path) => Canonical(Path.GetExtension(path)) switch
        {
            "png" or "jpeg" or "webp" or "heic" or "heif" or "tiff" or "avif" or "bmp" or "gif" or "svg" => "image",
            "mp4" or "mov" or "m4v" or "mkv" or "webm" or "avi" or "wmv" => "video",
            "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" or "aiff" or "wma" => "audio",
            "pdf" => "pdf", "epub" => "epub",
            "ppt" or "pptx" or "odp" => "presentation",
            "xls" or "xlsx" or "ods" or "csv" or "tsv" => "spreadsheet",
            "doc" or "docx" or "odt" or "rtf" or "txt" or "md" or "html" or "htm" or "pages" => "document",
            _ => ""
        };
        string family = Kind(paths[0]);
        return paths.All(path => Kind(path) == family) ? family : "";
    }
    private static string[] ReferenceFormats(IReadOnlyList<string> paths) => Family(paths) switch
    {
        "image" => ["png", "webp", "heic", "tiff", "avif", "bmp", "pdf"],
        "video" => ["mp4", "mkv", "webm", "avi", "wmv", "gif", "mp3"],
        "audio" => ["m4a", "wav", "flac", "ogg", "opus", "aiff", "wma"],
        "pdf" => ["docx", "pptx", "xlsx", "jpeg", "png", "epub", "txt", "md"],
        "document" => ["docx", "pdf", "txt", "md", "html", "epub", "png", "jpeg"],
        "presentation" => ["pdf", "jpeg", "png"], "spreadsheet" => ["ods", "csv", "tsv", "pdf"],
        "epub" => ["pdf", "docx", "txt", "md", "html"], _ => []
    };
    private static string[] ReferenceTools(IReadOnlyList<string> paths) => Family(paths) switch
    {
        "image" => ["image.compress", "image.metadata", "image.editor", "image.annotate", "image.background", "image.crop", "image.redact"],
        "video" => ["video.compress", "video.metadata", "video.mute", "video.trim", "video.crop", "video.speed", "video.snapshot", "video.split", "video.redact"],
        "audio" => ["audio.compress", "audio.metadata", "audio.normalize", "audio.visualizer", "audio.trim", "audio.channels", "audio.bleep"],
        "pdf" => ["pdf.compress", "pdf.metadata", "pdf.split", "pdf.organize"], _ => []
    };
}
