using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

/// <summary>Opt-in production capture over exhaustive owned-scene guards. Guard cost is reported, never hidden.</summary>
[Collection("Desktop")]
public sealed class NativeBackingStartupTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [NativeLiquidGlassFact]
    public Task Five_cold_and_thirty_warm_backing_activations_report_end_to_end_phases() => desktop.RunAsync(async () =>
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "single-backing-native",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(folder);
        var samples = new List<Sample>();
        var settled = new List<SettledFrame>();
        GlassOwnedScene? owned = null;
        GuardedSession? active = null;
        TaskCompletionSource<GlassStartupTiming>? first = null;
        var dispatcher = Dispatcher.CurrentDispatcher;
        double factoryGuardMilliseconds = 0, lastQueue = 0, lastUi = 0;
        int presented = 0;
        byte centerRed = 0;
        Exception? failure = null;
        ILiveGlassSession? Create(GlassScene scene, int x, int y)
        {
            var guardClock = Stopwatch.StartNew();
            dispatcher.Invoke(() => GlassCompositorCapture.RequireOwned(owned!, 0,
                Path.Combine(folder, "create.preflight.json"), scene.PaddingPixels));
            factoryGuardMilliseconds += guardClock.Elapsed.TotalMilliseconds;
            var native = StudioGlassSession.TryCreate(scene, x, y);
            return native is null ? null : active = new GuardedSession(native, () => dispatcher.Invoke(() =>
                GlassCompositorCapture.RequireOwned(owned!, 0, Path.Combine(folder, "frame.preflight.json"), scene.PaddingPixels)));
        }
        var wheel = new WheelWindow(new OperationCatalog([]), null, null, null, Create);
        wheel.ApplySettings(new CitrusSettings { ReduceMotion = true, SoundEnabled = false });
        owned = new GlassOwnedScene(wheel);
        var live = (LiveWheelGlass)typeof(WheelWindow).GetField("glass", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel)!;
        var productPresented = live.FramePresented;
        live.FramePresented = (scene, pixels) =>
        {
            productPresented?.Invoke(scene, pixels);
            presented++;
            int offset = ((int)(184 * scene.Scale) * scene.PixelWidth + (int)(184 * scene.Scale)) * 4;
            centerRed = pixels[offset + 2];
        };
        live.PresentationMeasured = (queue, work) => { lastQueue = queue; lastUi = work; };
        live.StartupMeasured = timing => first?.TrySetResult(timing);
        try
        {
            SingleBackingVisualEvidenceTests.Populate(wheel);
            owned.SetSolidPattern(false);
            owned.Show(activateOwnedBackground: true);
            var surface = (FrameworkElement)wheel.FindName("Surface");
            double Scale()
            {
                Point origin = surface.PointToScreen(default);
                return Math.Round(surface.PointToScreen(new Point(368, 0)).X - origin.X) / 368;
            }
            await owned.CenterCaptureTargetWithPaddingAsync(_ => StudioGlassMath.CapturePaddingPixels(GlassMaterial.Wheel, Scale()));
            double rasterScale = Scale();
            var scene = GlassScene.WheelPresentation(7, rasterScale, false);
            Assert.Equal(2, scene.Regions.Count);
            GlassCompositorCapture.RequireOwned(owned, 0, Path.Combine(folder, "fixture.preflight.json"), scene.PaddingPixels);
            Point position = surface.PointToScreen(default);
            int screenX = (int)Math.Round(position.X), screenY = (int)Math.Round(position.Y);
            for (int index = 0; index < 35; index++)
            {
                bool cold = index < 5;
                live.Stop();
                await live.Completion.WaitAsync(TimeSpan.FromSeconds(10));
                if (cold) StudioGlassRuntime.Shared.ResetForTesting();
                else if (index == 5) await StudioGlassRuntime.Shared.PrewarmAsync(scene);
                factoryGuardMilliseconds = 0;
                first = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var foregroundBoundary = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler rendering = null!;
                rendering = (_, _) =>
                {
                    CompositionTarget.Rendering -= rendering;
                    foregroundBoundary.TrySetResult(Stopwatch.GetTimestamp());
                };
                CompositionTarget.Rendering += rendering;
                // Synthetic admission intentionally isolates the production optical lifecycle.
                // Physical modifier/Explorer initiation is a separate acceptance check.
                long requested = Stopwatch.GetTimestamp();
                try
                {
                    live.SetActivationTiming(requested, requested);
                    live.SetPetalCount(7);
                    live.SetNavigationVisible(false);
                    live.StartWheelPresentation(screenX, screenY, scene.PixelWidth, false, false, owned.CaptureTargetHandle);
                    GlassStartupTiming timing;
                    try
                    {
                        Task completed = await Task.WhenAny(first.Task, live.Completion).WaitAsync(TimeSpan.FromSeconds(30));
                        if (completed != first.Task)
                        {
                            await live.Completion;
                            throw new InvalidOperationException($"Activation {index + 1} ended before its first bitmap. Controller: {live.LastFailure}; session: {active?.Diagnostic}");
                        }
                        timing = await first.Task;
                    }
                    catch (TimeoutException error)
                    { throw new TimeoutException($"Activation {index + 1} did not present. Controller: {live.LastFailure}; session: {active?.Diagnostic}", error); }
                    long foregroundRenderedAt = await foregroundBoundary.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Assert.True(foregroundRenderedAt >= requested);
                    Assert.True(timing.Pending >= timing.Requested);
                    Assert.True(timing.Presented >= timing.Pending);
                    Assert.True(timing.RenderCompleted >= timing.FirstFrame);
                    Assert.True(timing.RendererReady > 0 && timing.CaptureInitialized > 0);
                    Assert.Equal(!cold, timing.PreparedResources);
                    samples.Add(new(cold ? "cold" : "warm", cold ? index + 1 : index - 4, timing,
                        foregroundRenderedAt, Stopwatch.GetElapsedTime(requested, foregroundRenderedAt).TotalMilliseconds,
                        timing.Milliseconds(timing.Pending), timing.Milliseconds(timing.Presented),
                        factoryGuardMilliseconds + active!.GuardMilliseconds,
                        timing.RenderMilliseconds + timing.ReadbackMilliseconds, lastQueue, lastUi));
                    WriteReport(completed: false);
                }
                finally { CompositionTarget.Rendering -= rendering; }
            }
            // Real changes to the owned desktop drive settled frames; cached-source polls do not count.
            for (int index = 0; index < 20; index++)
            {
                int previous = presented;
                bool light = index % 2 == 0;
                owned.SetSolidPattern(light);
                // The pale neutral .68 undercoat maps owned white to about235R and black to153R.
                await WaitUntil(() => presented > previous && (light ? centerRed >= 230 : centerRed <= 170),
                    () => $"Expected owned {(light ? "white" : "black")} source; R={centerRed}; {active?.Diagnostic}");
                var timing = active!.StartupTiming;
                settled.Add(new(timing.RenderMilliseconds, timing.ReadbackMilliseconds, lastQueue, lastUi));
            }
            Assert.Equal(5, samples.Count(sample => sample.Mode == "cold"));
            Assert.Equal(30, samples.Count(sample => sample.Mode == "warm"));
            Assert.Equal(20, settled.Count);
            output.WriteLine($"Guarded native cold p95={Percentile(samples.Where(sample => sample.Mode == "cold").Select(sample => sample.FirstLiveMs), .95):F2} ms; " +
                $"warm p95={Percentile(samples.Where(sample => sample.Mode == "warm").Select(sample => sample.FirstLiveMs), .95):F2} ms. " +
                "End-to-end values include exhaustive test-only ownership guards and do not qualify uninstrumented user latency.");
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            live.Stop();
            try { await live.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { owned.Dispose(); WriteReport(completed: failure is null && samples.Count == 35 && settled.Count == 20); }
        }
        void WriteReport(bool completed)
        {
            object Distribution(IEnumerable<double> values)
            {
                var data = values.ToArray();
                return new { Count = data.Length, MedianMilliseconds = Percentile(data, .5), P95Milliseconds = Percentile(data, .95) };
            }
            File.WriteAllText(Path.Combine(folder, "startup-timing.json"), JsonSerializer.Serialize(new
            {
                Completed = completed, Failure = failure?.ToString(), Stopwatch.Frequency,
                Renderer = "Production LiveWheelGlass and StudioGlassSession; actual WheelWindow foreground",
                Admission = "Synthetic timestamps at optical activation; no Explorer/modifier initiation",
                ColdScope = "D3D device and GPU allocation cache reset; immutable shader bytecode and CPU backing scene remain resident",
                WarmScope = "GPU backing allocation prepared/reused; WGC capture starts fresh for every activation",
                CapturedContent = "Owned solid background only, with full padded input/desktop/z-order guard before every render",
                LatencyQualification = "Instrumented native latency includes exhaustive test-only guards. Do not subtract guards to claim raw acceptance.",
                PresentationQualification = "Pending is state installation; Presented is bitmap installation; WpfRenderBoundary is a subsequent CompositionTarget.Rendering acknowledgement, not DWM scanout or first visible pixel proof",
                UninstrumentedStartupTargetQualified = false,
                TargetsMilliseconds = new { Pending = 16.7, WarmLive = 100, ColdLive = 250, SettledTotal = 16.7 },
                Cold = Distribution(samples.Where(sample => sample.Mode == "cold").Select(sample => sample.FirstLiveMs)),
                Warm = Distribution(samples.Where(sample => sample.Mode == "warm").Select(sample => sample.FirstLiveMs)),
                PendingInstall = Distribution(samples.Select(sample => sample.PendingMs)),
                ForegroundWpfRenderBoundary = Distribution(samples.Select(sample => sample.ForegroundRenderBoundaryMs)),
                TestGuard = Distribution(samples.Select(sample => sample.TestGuardMs)),
                SettledCompletedFrame = Distribution(settled.Select(frame => frame.RenderMs + frame.ReadbackMs + frame.QueueMs + frame.UiMs)),
                Samples = samples, SettledFrames = settled
            }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        }
    }, TimeSpan.FromMinutes(10));

    private static double Percentile(IEnumerable<double> values, double fraction)
    {
        var data = values.Order().ToArray();
        if (data.Length == 0) return double.NaN;
        if (fraction == .5) return data.Length % 2 == 0 ? (data[data.Length / 2 - 1] + data[data.Length / 2]) / 2 : data[data.Length / 2];
        return data[Math.Clamp((int)Math.Ceiling(data.Length * fraction) - 1, 0, data.Length - 1)];
    }
    private static async Task WaitUntil(Func<bool> ready, Func<string> diagnostic)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(10);
        Assert.True(ready(), diagnostic());
    }
    private sealed record Sample(string Mode, int Index, GlassStartupTiming Phases, long WpfRenderBoundaryTicks, double ForegroundRenderBoundaryMs, double PendingMs, double FirstLiveMs,
        double TestGuardMs, double CompletedGpuMs, double QueueMs, double UiMs);
    private sealed record SettledFrame(double RenderMs, double ReadbackMs, double QueueMs, double UiMs);
    private sealed class GuardedSession(StudioGlassSession inner, Action guard) : ILiveGlassSession, IGlassSessionTiming
    {
        public GlassScene Scene => inner.Scene;
        public GlassSessionTiming StartupTiming => ((IGlassSessionTiming)inner).StartupTiming;
        public double GuardMilliseconds { get; private set; }
        public string Diagnostic => inner.Diagnostic;
        public bool TryRenderFrame(GlassFrameState state, byte[] destination)
        {
            var clock = Stopwatch.StartNew(); guard(); GuardMilliseconds += clock.Elapsed.TotalMilliseconds;
            return inner.TryRenderFrame(state, destination);
        }
        public void Dispose() => inner.Dispose();
    }
}
