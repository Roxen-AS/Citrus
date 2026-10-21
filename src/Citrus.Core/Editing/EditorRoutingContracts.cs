using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public enum EditorSubmissionShape { PerRoot, Group }
public sealed record EditorRoute(string OperationId, string EntryTool, EditorSubmissionShape Shape);
public sealed record EditorOpenRequest(OperationDescriptor Operation, ImmutableArray<string> InitialPaths, string EntryTool, OperationOptions InitialOptions, string? DestinationDirectory, SubmissionOrigin Origin);
public enum EditorImportRole { Root, Auxiliary }
public sealed record EditorImport(SourceSnapshot Source, EditorImportRole Role);
public sealed record EditorSubmissionItem(Guid Id, ImmutableArray<Guid> RootSourceIds, EditorDocument Document);
public sealed record EditorSubmissionBatch(EditorOpenRequest Request, ImmutableArray<SourceSnapshot> InitialSources, ImmutableArray<string> ExcludedInitialPaths, ImmutableArray<EditorImport> Imports, ImmutableArray<Guid> IncludedRootIds, ImmutableArray<EditorSubmissionItem> Items);
public enum ImageBatchGeometry { Relative, Pixels }
public sealed record ImageResizeIntent(int? Width, int? Height, bool PreserveAspectRatio, bool AllowUpscale);
public sealed record ImageBatchTemplate(EditorDocument Document, SizeI RootSize, ImageBatchGeometry Geometry, ImageResizeIntent? Resize, bool CopyLayers, bool PreserveSourceFormat);
public interface IEditorAdmissionInspector
{
    Task<ImmutableArray<ValidationIssue>> ValidateAsync(OperationRequest request, CancellationToken ct);
}
