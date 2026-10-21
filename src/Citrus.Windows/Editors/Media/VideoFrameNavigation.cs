using System.Numerics;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors.Media;

/// <summary>Keep exact presentation timestamps when a bounded preview window is reopened.</summary>
internal static class VideoFrameNavigation
{
    public static RationalTime SourcePosition(MediaClip clip, RationalTime output) => Add(clip.Range.Start, Multiply(output, Speed(clip.Speed)));
    public static RationalTime OutputPosition(MediaClip clip, RationalTime source) => Divide(Subtract(source, clip.Range.Start), Speed(clip.Speed));
    private static RationalTime Speed(double speed) => new(checked((long)((decimal)speed * 1_000_000m)), 1_000_000);
    private static RationalTime Add(RationalTime a, RationalTime b) => Make((BigInteger)a.Numerator * b.Denominator + (BigInteger)b.Numerator * a.Denominator, (BigInteger)a.Denominator * b.Denominator);
    private static RationalTime Subtract(RationalTime a, RationalTime b) => Make((BigInteger)a.Numerator * b.Denominator - (BigInteger)b.Numerator * a.Denominator, (BigInteger)a.Denominator * b.Denominator);
    private static RationalTime Multiply(RationalTime a, RationalTime b) => Make((BigInteger)a.Numerator * b.Numerator, (BigInteger)a.Denominator * b.Denominator);
    private static RationalTime Divide(RationalTime a, RationalTime b) => Make((BigInteger)a.Numerator * b.Denominator, (BigInteger)a.Denominator * b.Numerator);
    private static RationalTime Make(BigInteger numerator, BigInteger denominator)
    {
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / gcd)), checked((int)(denominator / gcd)));
    }
}
