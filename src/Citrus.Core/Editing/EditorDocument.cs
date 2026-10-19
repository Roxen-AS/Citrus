using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public sealed record EditorDocument(int SchemaVersion, long Revision, ImmutableArray<SourceSnapshot> Sources, ImmutableArray<FontArtifact> Fonts, EditorRecipe Recipe, ExportPolicy Export) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (SchemaVersion != 1) throw new System.Text.Json.JsonException("Only editor schema version 1 is supported.");
    }
}
public sealed record ExportPolicy(string FormatId, string? DestinationDirectory, string OutputStem, CompressionGoal Compression, MetadataPolicy Metadata, Rgba32 Matte);
public enum MetadataPolicy { PreserveSupported, StripDescriptive, StripAll }
public readonly record struct Rgba32(byte R, byte G, byte B, byte A);
public readonly record struct PointD(double X, double Y);
public readonly record struct RectD(double X, double Y, double Width, double Height);
public readonly record struct SizeI(int Width, int Height);
public sealed record ValidationIssue(string FieldPath, string Message);
