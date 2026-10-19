using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public sealed record FontArtifact(Guid Id, string OriginalPath, long Length, string Sha256, int FaceIndex, string Family, string FaceName);
public interface IFontArtifactService
{
    Task<FontArtifact> CaptureAsync(string fontFile, int faceIndex, CancellationToken ct);
    Task<ImmutableDictionary<Guid, string>> StageVerifiedAsync(ImmutableArray<FontArtifact> fonts, string privateDirectory, CancellationToken ct);
}
