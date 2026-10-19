using Citrus.Core.Editing;

namespace Citrus.Core;

public enum FileKind { Unknown, Directory, Image, Audio, Video, Pdf, Subtitle, Archive }
public enum OperationMode { Convert, Tools, Actions }

public sealed record OperationDescriptor(string Id, string Label, OperationMode Mode, string? OutputExtension, bool IsGroup, bool NeedsOptions);

public sealed record OperationOptions
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int Quality { get; init; } = 85;
    public int RotationDegrees { get; init; } = 90;
    public double StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public double Speed { get; init; } = 1;
    public double NormalizeLufs { get; init; } = -16;
    public string? PageSelection { get; init; }
    public bool PreserveAspectRatio { get; init; } = true;
}

public sealed record OperationRequest(OperationDescriptor Operation, IReadOnlyList<string> Inputs, OperationOptions Options, string? DestinationDirectory, EditorDocument? Editor = null);
public sealed record ConversionProgress(double? Fraction, string Message);
public sealed record ConversionResult(IReadOnlyList<string> Outputs, CompressionOutcome? Compression = null);

public interface IConversionEngine
{
    IReadOnlyList<OperationDescriptor> Operations { get; }
    bool CanExecute(OperationRequest request);
    Task<ConversionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken);
}

public enum ConversionJobState { Queued, Running, Succeeded, Failed, Canceled }
