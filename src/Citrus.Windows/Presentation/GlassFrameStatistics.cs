using System.Diagnostics;

namespace Citrus.Windows.Presentation;

/// <summary>Opt-in bounded numeric samples; contains no pixels, payload data or per-frame file IO.</summary>
internal sealed class GlassFrameStatistics
{
    private const int Capacity = 1024;
    private readonly double[] completed = new double[Capacity], queue = new double[Capacity], ui = new double[Capacity];
    private int total, submissions;
    internal void Submitted() => submissions++;
    internal void Presented(long submittedAt, double queueMs, double uiMs)
    {
        int index = total++ % Capacity;
        completed[index] = Stopwatch.GetElapsedTime(submittedAt).TotalMilliseconds;
        queue[index] = queueMs; ui[index] = uiMs;
    }
    internal object Summary(long admitted)
    {
        int count = Math.Min(total, Capacity);
        object Distribution(double[] samples)
        {
            var sorted = samples.AsSpan(0, count).ToArray(); Array.Sort(sorted);
            return new { Count = count, MedianMs = count == 0 ? double.NaN : sorted[count / 2],
                P95Ms = count == 0 ? double.NaN : sorted[Math.Clamp((int)Math.Ceiling(count * .95) - 1, 0, count - 1)] };
        }
        return new { Kind = "Frames", ActivationId = admitted, Submissions = submissions, Presented = total,
            SampleCapacity = Capacity, CompletedFrame = Distribution(completed), DispatcherQueue = Distribution(queue),
            UiPublication = Distribution(ui), Qualification = "Bounded latest completed submission-to-bitmap samples; not capture-to-scanout latency or GPU timestamp queries" };
    }
}
