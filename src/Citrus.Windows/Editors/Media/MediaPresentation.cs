using System;
using System.Runtime.InteropServices;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Media;

/// <summary>Pure presentation boundaries, shared by the native control and regression tests.</summary>
internal static class MediaPresentation
{
    // BitmapSource.Create copies synchronously. Only that read-only use of the backing array is
    // permitted; no additional managed BGRA allocation or retention beyond the presentation callback.
    internal static byte[] Pixels(PreviewFrame frame) => ImmutableCollectionsMarshal.AsArray(frame.Bgra32)
        ?? throw new ArgumentException("A complete frame payload is required.", nameof(frame));

    internal static RationalTime SeekPosition(double seconds, TimeRange range)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (range.Start.CompareTo(range.End) >= 0) throw new ArgumentException("A nonempty selection is required.", nameof(range));
        if (seconds <= (double)range.Start.Numerator / range.Start.Denominator) return range.Start;
        if (seconds >= (double)range.End.Numerator / range.End.Denominator) return range.End;
        var rounded = new RationalTime(checked((long)Math.Round(seconds * 1_000_000)), 1_000_000);
        if (rounded.CompareTo(range.Start) < 0) return range.Start;
        // End is an explicit pause sentinel; it is never passed to half-open SeekAsync.
        return rounded.CompareTo(range.End) >= 0 ? range.End : rounded;
    }
}
