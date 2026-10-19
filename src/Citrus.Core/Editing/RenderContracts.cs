using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public sealed record PreviewRequest(long Generation, SizeI Bounds, RationalTime? Time, Guid? PageId, RectD? SourceTile);
public sealed record PreviewFrame(long Revision, long Generation, SizeI Size, int Stride, ImmutableArray<byte> Bgra32, RationalTime? ActualTime, SourceFrameRef? FrameIdentity, ImmutableArray<string> Warnings);
public enum ExportShape { File, Directory }
public sealed record StagedOutput(string RelativePath, long Bytes);
public sealed record StagedExport(ExportShape Shape, ImmutableArray<StagedOutput> Files, CompressionOutcome Compression, ImmutableArray<string> Warnings);
public sealed record ExportTarget(string Directory, string Stem, string? Extension, ExportShape Shape);
public sealed record EditorCapability(string OperationId, string Family, ImmutableArray<string> RequiredCapabilities, bool Available, string? Reason);
public interface IEditorRenderer
{
    Task<PreviewFrame> RenderPreviewAsync(EditorDocument document, PreviewRequest request, CancellationToken ct);
    Task<StagedExport> RenderExportAsync(EditorDocument document, string privateDirectory, IProgress<ConversionProgress>? progress, CancellationToken ct);
}
public interface IEditorPayloadValidator
{
    Task ValidateAsync(OperationRequest request, StagedExport staged, string privateDirectory, CancellationToken ct);
}
