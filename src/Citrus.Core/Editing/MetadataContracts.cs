using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public sealed record MetadataField(string Scope, string Key, string DisplayName, MetadataValue Value, bool Writable);
public sealed record MetadataEdit(string Scope, string Key, MetadataEditAction Action, MetadataValue? Value);
public enum MetadataEditAction { Set, Remove }
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextValue), "text")]
[JsonDerivedType(typeof(IntegerValue), "integer")]
[JsonDerivedType(typeof(RealValue), "real")]
[JsonDerivedType(typeof(RationalValue), "rational")]
[JsonDerivedType(typeof(CaptureTimeValue), "captureTime")]
[JsonDerivedType(typeof(GpsValue), "gps")]
public abstract record MetadataValue;
public sealed record TextValue(string Value) : MetadataValue;
public sealed record IntegerValue(long Value) : MetadataValue;
public sealed record RealValue(double Value) : MetadataValue;
public sealed record RationalValue(long Numerator, long Denominator) : MetadataValue;
public sealed record CaptureTimeValue(DateTime Local, TimeSpan? Offset) : MetadataValue;
public sealed record GpsValue(double Latitude, double Longitude, double? AltitudeMeters) : MetadataValue;
public interface IMetadataService
{
    Task<ImmutableArray<MetadataField>> ReadAsync(SourceSnapshot source, CancellationToken ct);
    ImmutableArray<ValidationIssue> ValidateEdits(ImmutableArray<MetadataField> fields, ImmutableArray<MetadataEdit> edits);
}
