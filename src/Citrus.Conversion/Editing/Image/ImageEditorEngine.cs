using Citrus.Core;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Image;

/// <summary>Typed-only adapter. Intentionally unregistered until the real editor host is integrated.</summary>
public sealed class ImageEditorEngine(IEditorRenderer renderer, EditorOutputPublisher publisher, IEditorAdmissionInspector inspector) : IConversionEngine
{
    private static readonly OperationDescriptor[] Supported =
    [new("image.resize", "Resize", OperationMode.Tools, null, false, true), new("image.rotate", "Rotate", OperationMode.Tools, null, false, true), new("image.compress", "Compress", OperationMode.Tools, null, false, true), new("image.editor", "Adjust", OperationMode.Tools, null, false, true), new("image.crop", "Crop", OperationMode.Tools, null, false, true)];
    public IReadOnlyList<OperationDescriptor> Operations { get; } = Array.AsReadOnly(Supported);
    internal static bool AcceptsDescriptor(OperationDescriptor descriptor) => Supported.Any(d => d.Id == descriptor.Id && d.Mode == descriptor.Mode && d.OutputExtension == descriptor.OutputExtension && d.IsGroup == descriptor.IsGroup && d.NeedsOptions == descriptor.NeedsOptions);
    public bool CanExecute(OperationRequest request)
    {
        if (!AcceptsDescriptor(request.Operation)) return false;
        if (request.Editor is null) return request.Operation.Id is "image.editor" or "image.crop" && request.Inputs.Count == 1 && ImageRasterSource.Supports(request.Inputs[0]) && DropValidation.Accepts(request.Inputs, OperationMode.Tools);
        try { ImageRecipeRenderer.Validate(request.Editor); EditorExportPolicy.Resolve(request); return request.Inputs.Count == 1 && ImageRasterSource.Supports(request.Inputs[0]) && DropValidation.Accepts(request.Inputs, OperationMode.Tools); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { return false; }
    }
    public async Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); if (request.Editor is null || !CanExecute(request)) throw new NotSupportedException("Choose an available typed image operation.");
        var issues = await inspector.ValidateAsync(request, ct).ConfigureAwait(false);
        if (!issues.IsEmpty) throw new InvalidDataException(string.Join(" ", issues.Select(i => i.Message)));
        string scope = Path.Combine(Path.GetTempPath(), "citrus-image-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var staged = await renderer.RenderExportAsync(request.Editor!, scope, progress, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); return await publisher.PublishAsync(request, staged, scope, ct).ConfigureAwait(false);
        }
        finally { if (Directory.Exists(scope)) Directory.Delete(scope, true); }
    }
}
