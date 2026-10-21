using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Citrus.Core.Editing;

public readonly record struct RationalTime : IComparable<RationalTime>
{
    [JsonConstructor]
    public RationalTime(long numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        var divisor = System.Numerics.BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = (long)(numerator / divisor);
        Denominator = (int)(denominator / divisor);
    }
    public long Numerator { get; }
    public int Denominator { get; }
    public int CompareTo(RationalTime other)
    {
        if (Denominator <= 0 || other.Denominator <= 0) throw new ArgumentException("Invalid rational time.");
        return ((System.Numerics.BigInteger)Numerator * other.Denominator).CompareTo((System.Numerics.BigInteger)other.Numerator * Denominator);
    }
}
public readonly record struct TimeRange(RationalTime Start, RationalTime End);
public sealed record MediaClip(Guid Id, Guid SourceId, int? VideoStream, int? AudioStream, TimeRange Range, double Speed);
public sealed record VideoRecipe(ImmutableArray<MediaClip> Clips, RectD? Crop, SizeI Canvas, ImageFit Fit, bool Mute, ImmutableArray<TimedMask> Masks, ImmutableArray<RationalTime> SplitAt, RationalTime? SnapshotAt, ImmutableArray<MetadataEdit> MetadataEdits) : EditorRecipe;
public sealed record TimedMask(Guid Id, TimeRange Range, MaskEffect Effect, Rgba32 Fill, double Radius, ImmutableArray<MaskKeyframe> Keys, MaskInterpolation Interpolation);
public sealed record MaskKeyframe(RationalTime Time, RectD Bounds);
public enum MaskInterpolation { Linear, Step }
public sealed record AudioRecipe(ImmutableArray<MediaClip> Clips, int SampleRate, ChannelMatrix Channels, ImmutableArray<BleepRegion> Bleeps, LoudnessSpec? Loudness, ImmutableArray<MetadataEdit> MetadataEdits) : EditorRecipe;
public sealed record ChannelMatrix(ImmutableArray<string> InputChannels, ImmutableArray<string> OutputChannels, ImmutableArray<double> Coefficients, bool LimitPeak);
public sealed record BleepRegion(Guid Id, TimeRange Range, BleepMode Mode, double FrequencyHz, double GainDbFs, int FadeMilliseconds, ImmutableArray<int> Channels);
public enum BleepMode { Silence, Tone }
public sealed record LoudnessSpec(double IntegratedLufs, double LoudnessRangeLu, double TruePeakDbTp);
public sealed record VisualizerRecipe(AudioRecipe Audio, VisualizerMode Mode, SizeI Size, int FramesPerSecond, Rgba32 Background, Rgba32 Foreground, Guid? BackgroundImageId, bool KeepAudio) : EditorRecipe;
public enum VisualizerMode { Waveform, Spectrum, StillWaveform }
public sealed record SourceFrameRef(Guid SourceId, int StreamIndex, long RawPts);
public sealed record FrameStamp(long RawPts, RationalTime SourceOffset);
public sealed record MediaSourceInfo(Guid SourceId, RationalTime PresentationOrigin, ImmutableArray<VideoStreamInfo> Video, ImmutableArray<AudioStreamInfo> Audio, RationalTime Duration);
public sealed record VideoStreamInfo(int Index, RationalTime TimeBase, long RawStartPts, ImmutableArray<FrameStamp> Frames, SizeI CodedSize, SizeI DisplaySize, double RotationDegrees, RationalTime SampleAspect, bool IsHdr)
{
    // Inspection retains a bounded initial window plus this exact final stamp, even for long/VFR inputs.
    public FrameStamp? LastFrame { get; init; }
    public RationalTime? PresentationEnd { get; init; }
}
public sealed record AudioStreamInfo(int Index, RationalTime TimeBase, long RawStartPts, int SampleRate, ImmutableArray<string> Channels, RationalTime Duration);
public enum RenderPurpose { Preview, Export }
