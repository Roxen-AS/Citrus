using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(QualityGoal), "quality")]
[JsonDerivedType(typeof(ByteGoal), "bytes")]
[JsonDerivedType(typeof(ReductionGoal), "reduction")]
public abstract record CompressionGoal;
public sealed record QualityGoal(int Value) : CompressionGoal;
public sealed record ByteGoal(long MaximumBytes) : CompressionGoal;
public sealed record ReductionGoal(decimal Percent) : CompressionGoal;
public sealed record CompressionOutcome(long OriginalBytes, long ActualBytes, long? TargetBytes, bool MetTarget, int Attempts);
