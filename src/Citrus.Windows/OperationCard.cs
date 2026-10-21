using System.Windows.Media;
using Citrus.Core;

namespace Citrus.Windows;

public sealed record OperationCard(OperationDescriptor Operation)
{
    public string Label => Operation.Label;
    public string Summary => Operation.Mode == OperationMode.Actions ? Describe(Operation)
        : (Operation.IsGroup ? "Combine inputs" : Operation.NeedsOptions ? "Adjust settings" : "Convert each file") +
          (Operation.OutputExtension is { } format ? " · " + format.TrimStart('.').ToUpperInvariant() + " output" : " · new copies");
    public Geometry Icon => (Geometry)Application.Current.FindResource("Icon." + Category);
    private string Category => Operation.Id switch
    {
        var id when id.Contains("resize", StringComparison.Ordinal) => "Resize",
        var id when id.Contains("rotate", StringComparison.Ordinal) => "Rotate",
        var id when id.StartsWith("image", StringComparison.Ordinal) => "Image",
        var id when id.StartsWith("audio", StringComparison.Ordinal) => "Audio",
        var id when id.StartsWith("video", StringComparison.Ordinal) || id.StartsWith("media", StringComparison.Ordinal) => "Video",
        var id when id.StartsWith("archive", StringComparison.Ordinal) => "Archive",
        var id when id.StartsWith("pdf", StringComparison.Ordinal) || id.StartsWith("subtitle", StringComparison.Ordinal) => "Text",
        _ => "Folder"
    };
    public static string Describe(OperationDescriptor operation) => operation.Id switch
    {
        "image.resize" or "video.resize" => "Set a width or height in pixels. Keep the aspect ratio to avoid stretching.",
        "image.compress" => "Choose image quality from 1–100. A new copy keeps your original safe.",
        "image.rotate" => "Turn your image clockwise by 90°, 180° or 270°.",
        "audio.trim" or "video.trim" => "Choose start and end times in seconds after the file is inspected.",
        "video.speed" => "Change playback speed from 0.25× to 4×.",
        "audio.normalize" => "Set a consistent loudness target for a new audio copy.",
        "archive.extract" => "Extract into a new folder. Stops at 100,000 entries or 20 GiB; nested archives stay packed.",
        "action.move" => "Move originals to a folder you choose. This changes their location.",
        "action.reveal" => "Show the included items in Explorer.",
        "action.copy" => "Copy included items to the Windows clipboard.",
        "action.float" => "Keep included items in a floating shelf for another drag.",
        var id when id.StartsWith("image.", StringComparison.Ordinal) || id == "images.pdf" => "Single-frame images and SVG input. Animated and multipage images are rejected.",
        var id when id.StartsWith("pdf.", StringComparison.Ordinal) => "Work with local PDF pages. Review page ranges and destination before starting.",
        _ => operation.Mode == OperationMode.Actions ? "Apply this action to all included items." : operation.IsGroup ? "Review the input order and save one grouped result." : "Create a local copy in the selected format."
    };
}
