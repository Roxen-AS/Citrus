using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Citrus.Conversion.Editing.Media;

namespace Citrus.Windows.Editors.Media;

/// <summary>Draws actual channel-aggregated PCM extrema. No synthesis and no animation subscription.</summary>
public sealed class WaveformView : Control
{
    public static readonly DependencyProperty BucketsProperty = DependencyProperty.Register(nameof(Buckets),
        typeof(ImmutableArray<WaveformBucket>), typeof(WaveformView),
        new FrameworkPropertyMetadata(ImmutableArray<WaveformBucket>.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    public ImmutableArray<WaveformBucket> Buckets
    { get => (ImmutableArray<WaveformBucket>)GetValue(BucketsProperty); set { if (!Buckets.Equals(value)) SetValue(BucketsProperty, value); } }
    public static readonly DependencyProperty StartFractionProperty = DependencyProperty.Register(nameof(StartFraction), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EndFractionProperty = DependencyProperty.Register(nameof(EndFraction), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double StartFraction { get => (double)GetValue(StartFractionProperty); set => SetValue(StartFractionProperty, value); }
    public double EndFraction { get => (double)GetValue(EndFractionProperty); set => SetValue(EndFractionProperty, value); }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var buckets = Buckets;
        if (buckets.IsDefaultOrEmpty || ActualWidth <= 0 || ActualHeight <= 0) return;
        var pen = new Pen(Foreground, System.Math.Max(1, ActualWidth / buckets.Length));
        double mid = ActualHeight / 2;
        for (int i = 0; i < buckets.Length; i++)
        {
            double x = ActualWidth * (StartFraction + (EndFraction - StartFraction) * (i + .5) / buckets.Length);
            drawing.DrawLine(pen, new(x, mid * (1 - buckets[i].Maximum)), new(x, mid * (1 - buckets[i].Minimum)));
        }
    }
}
