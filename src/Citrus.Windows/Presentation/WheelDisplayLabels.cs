using Citrus.Core;
namespace Citrus.Windows.Presentation;
internal static class WheelDisplayLabels
{
    internal static string Petal(OperationDescriptor operation) => operation.Id switch
    {
        "image.convert.gif" => "GIF", "image.convert.jpeg" => "JPG",
        "image.editor" => "EDIT", "image.annotate" => "ANNOTATE", "image.redact" => "REDACT",
        "image.metadata" => "METADATA", "image.strip" => "STRIP", "image.background" => "ADD BG", "image.crop" => "CROP", "image.resize" => "RESIZE", "image.rotate" => "ROTATE", "image.compress" => "COMPRESS",
        "action.copy" => "COPY", "action.move" => "MOVE", "action.paths" => "COPY PATHS", "action.float" => "FLOAT", "action.terminal" => "TERMINAL", "action.reveal" => "REVEAL",
        "video.crop" => "CROP", "video.trim" => "TRIM", "video.resize" => "RESIZE", "video.speed" => "SPEED", "video.audio" => "EXTRACT",
        "video.compress" or "audio.compress" or "pdf.compress" => "COMPRESS",
        "video.metadata" or "audio.metadata" or "pdf.metadata" => "METADATA",
        "video.mute" => "MUTE", "video.snapshot" => "SNAPSHOT", "video.split" => "SPLIT", "video.redact" => "REDACT",
        "audio.channels" => "CHANNELS", "audio.bleep" => "BLEEP", "audio.visualizer" => "VISUALIZE",
        "audio.trim" => "TRIM", "audio.normalize" => "NORMALIZE", "pdf.organize" or "pdf.editor" => "ORGANIZE", "pdf.merge" => "MERGE", "pdf.split" => "SPLIT", "archive.extract" => "EXTRACT", "archive.gzip" => "GZIP",
        _ => operation.Mode == OperationMode.Convert
            ? string.IsNullOrEmpty(operation.OutputExtension) ? operation.Label : operation.OutputExtension.TrimStart('.').ToUpperInvariant()
            : operation.Label.TrimEnd('…').ToUpperInvariant()
    };
    internal static string Description(OperationDescriptor operation) => operation.Mode == OperationMode.Convert
        ? "Convert to " + Petal(operation) : operation.Id == "archive.extract" ? "Extract archive" : operation.Label.TrimEnd('…');
}
