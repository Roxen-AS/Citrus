using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Citrus.Windows.Presentation;

/// <summary>Opt-in bounded development measurements; contains timings, never desktop content.</summary>
internal static class GlassStartupLog
{
    private static readonly object gate = new();
    private static int records;
    internal static bool Enabled => Environment.GetEnvironmentVariable("CITRUS_GLASS_STARTUP_LOG") is { Length: > 0 } path && Path.IsPathFullyQualified(path);

    internal static void RecordBoundaryIfEnabled(long requested, long admitted, long rendered, string phase)
        => WriteIfEnabled(() => new
        {
            Kind = "FirstWpfBoundary", ActivationId = admitted, Phase = phase,
            RequestToBoundaryMs = Span(requested, rendered), AdmissionToBoundaryMs = Span(admitted, rendered),
            Qualification = "First WPF Rendering callback after backing installation; not DWM scanout or pixel acceptance"
        });

    internal static void RecordFramesIfEnabled(long admitted, GlassFrameStatistics statistics)
        => WriteIfEnabled(() => statistics.Summary(admitted));

    internal static void RecordIfEnabled(GlassStartupTiming timing)
        => WriteIfEnabled(() => new
        {
            Kind = "Live",
            ActivationId = timing.Admitted,
            RequestToPendingMs = timing.Milliseconds(timing.Pending),
            RequestToBitmapMs = timing.Milliseconds(timing.Presented),
            AdmissionToBitmapMs = Span(timing.Admitted, timing.Presented),
            SceneMs = Span(timing.Excluded, timing.SceneReady),
            CaptureInitializationMs = Span(timing.CaptureStarted == 0 ? timing.SceneReady : timing.CaptureStarted, timing.CaptureInitialized),
            RendererPreparationMs = Span(timing.CaptureInitialized, timing.RendererReady),
            WaitForFirstTextureMs = Span(timing.RendererReady, timing.FirstFrame),
            timing.RenderMilliseconds, timing.ReadbackMilliseconds, timing.PreparedResources,
            Qualification = "Production bitmap publication; not DWM scanout or pixel acceptance. Readback includes GPU completion wait."
        });

    internal static void RecordBootstrapIfEnabled(long requested, long admitted, long started, long queued, long presented, GlassBootstrapWork work)
        => WriteIfEnabled(() => new
        {
            Kind = "Bootstrap",
            ActivationId = admitted,
            RequestToBootstrapMs = Span(requested, presented), AdmissionToBootstrapMs = Span(admitted, presented),
            PreparationMs = Span(started, queued), QueueAndPublicationMs = Span(queued, presented),
            work.CaptureMilliseconds, work.RenderMilliseconds, work.CompletionMilliseconds, work.PreparedResources,
            work.ResourcePreparationMilliseconds, work.CapturePreparationMilliseconds,
            work.CaptureBlitMilliseconds, work.CaptureCopyMilliseconds, work.CaptureClearMilliseconds,
            Qualification = "Fresh admission snapshot bitmap installed before live capture initialization; not DWM scanout proof"
        });

    internal static void RecordAdmissionIfEnabled(long admitted, long source, long content, long target, long optical, long shown)
        => WriteIfEnabled(() => new
        {
            Kind = "Admission", ActivationId = admitted, SourceIdentityMs = Span(admitted, source), ContentSetupMs = Span(source, content),
            TargetHandleMs = Span(content, target), OpticalRequestMs = Span(target, optical),
            TargetShowMs = Span(optical, shown), AdmissionToTargetShownMs = Span(admitted, shown)
        });

    private static double Span(long from, long to) => from == 0 || to == 0 ? double.NaN : Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    private static void WriteIfEnabled(Func<object> record)
    {
        string? path = Environment.GetEnvironmentVariable("CITRUS_GLASS_STARTUP_LOG");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            Interlocked.Increment(ref records) > 256) return;
        _ = Task.Run(() =>
        {
            try
            {
                string entry = JsonSerializer.Serialize(record(), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
                lock (gate) File.AppendAllText(path, entry + Environment.NewLine);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { Trace.WriteLine($"Glass timing log unavailable: {error.GetType().Name}"); }
        });
    }
}
