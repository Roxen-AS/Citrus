using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public sealed record SourceSnapshot(Guid Id, string Path, long Length, long LastWriteUtcTicks, string Sha256);
public sealed record SourceCaptureResult(string Path, SourceSnapshot? Snapshot, string? Error);
public interface ISourceSnapshotService
{
    Task<ImmutableArray<SourceCaptureResult>> CaptureAsync(ImmutableArray<string> paths, CancellationToken ct);
    Task<ImmutableDictionary<Guid, string>> StageVerifiedAsync(ImmutableArray<SourceSnapshot> sources, string privateDirectory, CancellationToken ct);
}
