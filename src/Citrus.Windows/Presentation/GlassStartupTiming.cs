using System.Diagnostics;

namespace Citrus.Windows.Presentation;

/// <summary>Monotonic phase timestamps for one admitted optical presentation.</summary>
internal sealed record GlassStartupTiming(long Requested, long Admitted, long Pending, long PreviousStopped,
    long Excluded, long SceneReady, long CaptureInitialized, long RendererReady, long FirstFrame,
    long RenderCompleted, long Presented, double RenderMilliseconds, double ReadbackMilliseconds,
    bool PreparedResources)
{
    internal long CaptureStarted { get; init; }
    internal double Milliseconds(long timestamp) => timestamp == 0 ? double.NaN
        : Stopwatch.GetElapsedTime(Requested, timestamp).TotalMilliseconds;
}

internal readonly record struct GlassSessionTiming(long CaptureInitialized = 0, long RendererReady = 0,
    long FirstFrame = 0, long RenderCompleted = 0, double RenderMilliseconds = 0,
    double ReadbackMilliseconds = 0, bool PreparedResources = false);

internal interface IGlassSessionTiming
{
    GlassSessionTiming StartupTiming { get; }
}
