using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;

namespace Citrus.Windows.Tests;

/// <summary>Opt-in native HWND and generated-input GPU measurements; reads no desktop pixels and injects no input.</summary>
[Collection("Desktop")]
public sealed class NativeWheelPerformanceMeasurementsTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [WheelPerformanceFact]
    public Task Fresh_target_and_generated_gpu_measurements_report_separate_costs() => desktop.RunAsync(async () =>
    {
        string folder = Environment.GetEnvironmentVariable("CITRUS_WHEEL_PERFORMANCE_RESULTS") is { Length: > 0 } requested && Path.IsPathFullyQualified(requested)
            ? requested : Path.Combine(AppContext.BaseDirectory, "TestResults", "wheel-performance");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"native-costs-{Environment.ProcessId}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.json");
        bool coldOnly = Environment.GetEnvironmentVariable("CITRUS_WHEEL_PERFORMANCE_COLD_ONLY") == "1";
        using var pollingDelay = new GlassRenderDelay();
        var targets = new List<TargetSample>();
        var frames = new List<GpuSample>();
        var allocations = new List<object>();
        Exception? failure = null;
        var wheel = new WheelWindow(new OperationCatalog([]));
        long prepareStarted = Stopwatch.GetTimestamp();
        wheel.PrepareHiddenPresentation();
        double hiddenWheelPreparationMs = Stopwatch.GetElapsedTime(prepareStarted).TotalMilliseconds;
        Assert.False(wheel.IsVisible);
        Assert.NotEqual(nint.Zero, new WindowInteropHelper(wheel).Handle);
        try
        {
            // Each activation creates a fresh real WinForms OLE target, as production does.
            // The first observation is process-first target creation, not a full cold app launch.
            for (int index = 0; index < (coldOnly ? 1 : 31); index++)
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                double handleMs = 0;
                var placements = new List<double>();
                var platform = new WheelDragCapturePlatform(form =>
                {
                    long phase = Stopwatch.GetTimestamp();
                    try { return WheelDragCapturePlatform.Default.CreateHandle(form); }
                    finally { handleMs += Stopwatch.GetElapsedTime(phase).TotalMilliseconds; }
                }, handle =>
                {
                    long phase = Stopwatch.GetTimestamp();
                    try { WheelDragCapturePlatform.Default.Place(handle); }
                    finally { placements.Add(Stopwatch.GetElapsedTime(phase).TotalMilliseconds); }
                });
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                var target = new WheelDragCapture(wheel, static (_, _) => { }, static _ => { }, static _ => { }, platform);
                long constructed = Stopwatch.GetTimestamp();
                try
                {
                    target.Start();
                    long shown = Stopwatch.GetTimestamp();
                    double placementBefore = placements.ElementAtOrDefault(0), placementAfter = placements.ElementAtOrDefault(1);
                    Assert.True(target.IsVisible);
                    Assert.NotEqual(nint.Zero, target.Handle);
                    target.Release(Task.CompletedTask);
                    long hidden = Stopwatch.GetTimestamp();
                    await target.Completion.WaitAsync(TimeSpan.FromSeconds(5));
                    long disposed = Stopwatch.GetTimestamp();
                    targets.Add(new(index == 0 ? "process-first-target" : "warm-target", index,
                        Span(start, constructed), Span(constructed, shown), Span(start, shown),
                        Span(shown, hidden), Span(hidden, disposed), GC.GetAllocatedBytesForCurrentThread() - allocated)
                    {
                        ManagedFormPreparationMs = Span(start, constructed) - handleMs,
                        NativeHandleAndOleRegistrationMs = handleMs, PlacementBeforeShowMs = placementBefore,
                        FormShowResidualMs = Span(constructed, shown) - placementBefore - placementAfter,
                        PlacementAfterShowMs = placementAfter, PlacementCallsAtShown = placements.Count
                    });
                    Assert.Equal(nint.Zero, target.Handle);
                    WriteReport(false);
                }
                finally { target.Release(Task.CompletedTask); await target.Completion.WaitAsync(TimeSpan.FromSeconds(5)); }
            }

            if (!coldOnly)
            {
                await Task.Run(D3D11StudioRenderer.PrecompileShaders);
                foreach (double dpi in new[] { 1d, 1.5d, 2d })
                {
                    long sceneStarted = Stopwatch.GetTimestamp();
                    var scene = GlassScene.WheelPresentation(0, WheelPresentationMetrics.Scale * dpi, true);
                    double sceneMs = Stopwatch.GetElapsedTime(sceneStarted).TotalMilliseconds;
                    byte[] source = GeneratedSource(scene), destination = new byte[scene.PixelWidth * scene.PixelHeight * 4];
                    var bitmap = new WriteableBitmap(scene.PixelWidth, scene.PixelHeight, 96, 96, PixelFormats.Pbgra32, null);
                    D3D11StudioRenderer? renderer = null;
                    try
                    {
                        long createStarted = Stopwatch.GetTimestamp();
                        renderer = await Task.Run(() => D3D11StudioRenderer.CreateForTesting(scene));
                        allocations.Add(new { Dpi = dpi, SceneMs = sceneMs, DeviceAndRendererMs = Stopwatch.GetElapsedTime(createStarted).TotalMilliseconds,
                            renderer.AllocationBytes, scene.PixelWidth, scene.PixelHeight, scene.CaptureWidth, scene.CaptureHeight, scene.PaddingPixels });
                        for (int frame = 0; frame < 125; frame++)
                        {
                            source[0] = (byte)frame; // Every observation is explicitly submitted as a changed full source.
                            var frameState = new GlassFrameState(NavigationVisible: true);
                            GpuWork work = await Task.Run(async () =>
                            {
                                long started = Stopwatch.GetTimestamp();
                                Assert.True(renderer.TrySubmitPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0, frameState, frame + 1));
                                long submitted = Stopwatch.GetTimestamp();
                                int polls = 0;
                                double pollingWaitMs = 0, maxPollingWaitMs = 0, completionCpuMs = 0, finalCompletionCpuMs = 0;
                                long completedStamp;
                                while (true)
                                {
                                    long completionCall = Stopwatch.GetTimestamp();
                                    bool ready = renderer.TryComplete(destination, out completedStamp);
                                    double callMs = Stopwatch.GetElapsedTime(completionCall).TotalMilliseconds;
                                    completionCpuMs += callMs;
                                    if (ready) { finalCompletionCpuMs = callMs; break; }
                                    polls++;
                                    if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
                                        throw new TimeoutException("Generated GPU frame did not complete.");
                                    long waitingAt = Stopwatch.GetTimestamp();
                                    await pollingDelay.WaitAsync(1);
                                    double waitMs = Stopwatch.GetElapsedTime(waitingAt).TotalMilliseconds;
                                    pollingWaitMs += waitMs; maxPollingWaitMs = Math.Max(maxPollingWaitMs, waitMs);
                                }
                                Assert.Equal(frame + 1, completedStamp);
                                long completed = Stopwatch.GetTimestamp();
                                return new GpuWork(started, submitted, completed, polls, pollingWaitMs, maxPollingWaitMs,
                                    completionCpuMs, finalCompletionCpuMs, renderer.LastReadbackMilliseconds);
                            });
                            long queuedAt = Stopwatch.GetTimestamp(), installed = 0, published = 0;
                            await Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                            {
                                installed = Stopwatch.GetTimestamp();
                                bitmap.WritePixels(new Int32Rect(0, 0, scene.PixelWidth, scene.PixelHeight), destination, scene.PixelWidth * 4, 0);
                                published = Stopwatch.GetTimestamp();
                            }, DispatcherPriority.Render);
                            frames.Add(new(dpi, frame < 5, frame, Span(work.Started, work.Submitted), Span(work.Submitted, work.Completed),
                                Span(queuedAt, installed), Span(installed, published), Span(work.Started, published), work.Polls,
                                work.PollingWaitMs, work.MaxPollingWaitMs, work.CompletionCpuMs, work.FinalCompletionCpuMs,
                                work.RendererReadbackElapsedMs));
                        }
                        Assert.Equal(0, renderer.PendingReadbackCount);
                        Assert.Equal(125, renderer.SourcePassCount);
                        Assert.Equal(125, renderer.OpticalPassCount);
                    }
                    finally
                    {
                        if (renderer is not null)
                        {
                            renderer.InvalidateReadbacks();
                            var drain = Stopwatch.StartNew();
                            while (!renderer.TryDiscardCompletedReadbacks() && drain.Elapsed < TimeSpan.FromSeconds(5)) await pollingDelay.WaitAsync(1);
                            renderer.Dispose();
                        }
                        Array.Clear(source); Array.Clear(destination);
                    }
                    WriteReport(false);
                }
            }
            WriteReport(true);
            output.WriteLine($"Native cost report: {path}");
            output.WriteLine($"Target creation + show: process-first {targets[0].CreationAndShowMs:F2} ms; warm p95 {Percentile(targets.Skip(1).Select(s => s.CreationAndShowMs), .95):F2} ms.");
        }
        catch (Exception error) { failure = error; WriteReport(false); throw; }
        finally { wheel.ReleasePresentation(); wheel.Close(); }

        void WriteReport(bool completed) => File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Completed = completed, Failure = failure?.ToString(), Environment.ProcessId, Stopwatch.Frequency,
            Scope = "Real hidden WheelWindow preparation and fresh production WinForms drag target. Generated production-size GPU input; actual WriteableBitmap publication. No desktop capture or injected input.",
            TargetColdScope = "First target within this newly launched manifested test process; WPF resources and hidden wheel are already prepared. Not a cold application activation measurement.",
            GpuScope = "Hardware D3D device with unchanged production scene/material at each DPI; generated source, single renderer, no WGC, no shared-device contention. Five first-use observations then120 steady frames per DPI. Runs uncapped to observe cost, without changing the production60FPS cadence. Publication is bitmap installation, not compositor scanout.",
            GpuTimingQualification = "CompletionAndReadback and RendererReadbackElapsed include scheduling/poll waits. TryCompleteCpu reports CPU-side API call elapsed time, including final mapping/copy, not thread CPU time or GPU timestamp duration. PollingWait records actual production GlassRenderDelay(1) wake intervals.",
            pollingDelay.HighResolutionAvailable,
            PhysicalExplorerQualified = false, FirstBlurredActivationQualified = false, FullColdLaunchQualified = false,
            HiddenWheelPreparationMs = hiddenWheelPreparationMs,
            TargetFirst = Distribution(targets.Take(1).Select(s => s.CreationAndShowMs)),
            TargetWarm = Distribution(targets.Skip(1).Select(s => s.CreationAndShowMs)),
            TargetWarmPhases = new
            {
                ManagedFormPreparation = Distribution(targets.Skip(1).Select(s => s.ManagedFormPreparationMs)),
                NativeHandleAndOleRegistration = Distribution(targets.Skip(1).Select(s => s.NativeHandleAndOleRegistrationMs)),
                PlacementBeforeShow = Distribution(targets.Skip(1).Select(s => s.PlacementBeforeShowMs)),
                FormShowResidual = Distribution(targets.Skip(1).Select(s => s.FormShowResidualMs)),
                PlacementAfterShow = Distribution(targets.Skip(1).Select(s => s.PlacementAfterShowMs))
            },
            SteadyByDpi = frames.Where(s => !s.FirstUse).GroupBy(s => s.Dpi).Select(group => new
            {
                Dpi = group.Key, Submission = Distribution(group.Select(s => s.SubmissionMs)),
                CompletionAndReadback = Distribution(group.Select(s => s.CompletionAndReadbackMs)),
                PollingWait = Distribution(group.Select(s => s.PollingWaitMs)),
                LongestPollWait = Distribution(group.Select(s => s.MaxPollingWaitMs)),
                TryCompleteCpu = Distribution(group.Select(s => s.CompletionCpuMs)),
                FinalTryCompleteCpu = Distribution(group.Select(s => s.FinalCompletionCpuMs)),
                RendererReadbackElapsed = Distribution(group.Select(s => s.RendererReadbackElapsedMs)),
                DispatcherQueue = Distribution(group.Select(s => s.DispatcherQueueMs)),
                Publication = Distribution(group.Select(s => s.PublicationMs)), Total = Distribution(group.Select(s => s.TotalMs))
            }),
            Allocations = allocations, Targets = targets, Frames = frames
        }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }, TimeSpan.FromMinutes(3));

    private static byte[] GeneratedSource(GlassScene scene)
    {
        var pixels = new byte[scene.CaptureWidth * scene.CaptureHeight * 4];
        for (int y = 0; y < scene.CaptureHeight; y++)
        for (int x = 0; x < scene.CaptureWidth; x++)
        {
            int p = (y * scene.CaptureWidth + x) * 4;
            pixels[p] = (byte)((x * 7 + y * 3) % 256); pixels[p + 1] = (byte)((x / 16 + y / 16) % 2 == 0 ? 230 : 20);
            pixels[p + 2] = (byte)((x + y) % 256); pixels[p + 3] = 255;
        }
        return pixels;
    }
    private static double Span(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
    private static object Distribution(IEnumerable<double> values)
    {
        var data = values.ToArray();
        return new { Count = data.Length, MedianMs = Percentile(data, .5), P95Ms = Percentile(data, .95) };
    }
    private static double Percentile(IEnumerable<double> values, double fraction)
    {
        var data = values.Order().ToArray();
        if (data.Length == 0) return double.NaN;
        if (fraction == .5) return data.Length % 2 == 0 ? (data[data.Length / 2 - 1] + data[data.Length / 2]) / 2 : data[data.Length / 2];
        return data[Math.Clamp((int)Math.Ceiling(data.Length * fraction) - 1, 0, data.Length - 1)];
    }
    private sealed record TargetSample(string Mode, int Index, double ConstructionMs, double ShowMs, double CreationAndShowMs,
        double HideAndRevokeMs, double ExclusionAndDisposalMs, long ManagedAllocationBytes)
    {
        public double ManagedFormPreparationMs { get; init; }
        public double NativeHandleAndOleRegistrationMs { get; init; }
        public double PlacementBeforeShowMs { get; init; }
        public double FormShowResidualMs { get; init; }
        public double PlacementAfterShowMs { get; init; }
        public int PlacementCallsAtShown { get; init; }
    }
    private sealed record GpuWork(long Started, long Submitted, long Completed, int Polls, double PollingWaitMs,
        double MaxPollingWaitMs, double CompletionCpuMs, double FinalCompletionCpuMs, double RendererReadbackElapsedMs);
    private sealed record GpuSample(double Dpi, bool FirstUse, int Index, double SubmissionMs, double CompletionAndReadbackMs,
        double DispatcherQueueMs, double PublicationMs, double TotalMs, int Polls, double PollingWaitMs,
        double MaxPollingWaitMs, double CompletionCpuMs, double FinalCompletionCpuMs, double RendererReadbackElapsedMs);
}

public sealed class WheelPerformanceFactAttribute : FactAttribute
{
    public WheelPerformanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CITRUS_RUN_WHEEL_PERFORMANCE") != "1")
            Skip = "Opt-in native target and generated-input GPU timing observations.";
    }
}
