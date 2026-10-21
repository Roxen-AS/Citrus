using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;
using Image = System.Windows.Controls.Image;
using Window = System.Windows.Window;
using Brushes = System.Windows.Media.Brushes;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class NativeBootstrapProofTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [Fact]
    public Task Owned_guard_exposes_an_awaitable_full_pixel_preflight_without_a_screen_read() => desktop.RunAsync(() =>
    {
        var method = AsyncPreflight();
        Assert.Equal(typeof(Task<GlassCaptureSnapshot>), method.ReturnType);
        Assert.Equal(new[] { typeof(GlassOwnedScene), typeof(nint), typeof(string), typeof(int) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
        return Task.CompletedTask;
    });

    [NativeLiquidGlassFact]
    public Task Five_cold_and_thirty_warm_snapshots_qualify_owned_bootstrap_pixels_and_report_phases() => desktop.RunAsync(async () =>
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "bootstrap-native", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(folder);
        var samples = new List<Sample>();
        var scene = GlassScene.WheelPresentation(0, 312 / 368d, true);
        NativeMethods.GetCursorPos(out var pointer);
        double monitorScale = NativeMethods.MonitorScale(pointer);
        var image = new Image { Stretch = Stretch.Fill };
        var target = new Window
        {
            Title = "Citrus owned bootstrap target", WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent,
            Width = scene.PixelWidth / monitorScale, Height = scene.PixelHeight / monitorScale,
            ShowActivated = false, ShowInTaskbar = false, Content = image
        };
        using var owned = new GlassOwnedScene(target);
        IDisposable? exclusion = null;
        Exception? failure = null;
        try
        {
            owned.SetSolidPattern(false);
            owned.Show(activateOwnedBackground: true);
            Assert.True(NativeMethods.SetWindowPos(owned.CaptureTargetHandle, new nint(-1), 0, 0,
                scene.PixelWidth, scene.PixelHeight, 0x12)); // NOACTIVATE | NOMOVE.
            await owned.CenterCaptureTargetWithPaddingAsync(_ => scene.PaddingPixels);
            // Placement can move the fixture onto a monitor whose DPI differs
            // from the pointer monitor used for the initial logical size.
            var settledDpi = VisualTreeHelper.GetDpi(target);
            target.Width = scene.PixelWidth / settledDpi.DpiScaleX;
            target.Height = scene.PixelHeight / settledDpi.DpiScaleY;
            target.UpdateLayout();
            Assert.True(NativeMethods.SetWindowPos(owned.CaptureTargetHandle, new nint(-1), 0, 0,
                scene.PixelWidth, scene.PixelHeight, 0x12));
            await owned.CenterCaptureTargetWithPaddingAsync(_ => scene.PaddingPixels);
            Assert.Equal(scene.PixelWidth, owned.Bounds.Width); Assert.Equal(scene.PixelHeight, owned.Bounds.Height);
            exclusion = StudioCaptureExclusion.TryAcquire([owned.CaptureTargetHandle]);
            Assert.NotNull(exclusion);
            Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out uint affinity)); Assert.Equal(0x11u, affinity);
            await Task.Run(D3D11StudioRenderer.PrecompileShaders);

            for (int index = 0; index < 35; index++)
            {
                bool cold = index < 5;
                if (cold) StudioGlassRuntime.Shared.ResetForTesting();
                else if (index == 5) await StudioGlassRuntime.Shared.PrewarmAsync(scene);
                bool light = index % 2 != 0;
                owned.SetSolidPattern(light);
                await Dispatcher.Yield(DispatcherPriority.Render);
                int flush = BackdropNativeMethods.DwmFlush();
                if (flush < 0) Marshal.ThrowExceptionForHR(flush);
                var bounds = owned.Bounds;
                long requestedAt = Stopwatch.GetTimestamp();
                var guard = Stopwatch.StartNew();
                // The same exhaustive native ownership walk runs off the WPF
                // dispatcher, keeping its owned windows responsive during it.
                await RequireOwned(owned, Path.Combine(folder, $"sample-{index + 1:00}.preflight.json"), scene.PaddingPixels);
                double guardMs = guard.Elapsed.TotalMilliseconds;
                Frame? frame = null;
                try
                {
                    frame = await Task.Run(() =>
                    {
                        long startedAt = Stopwatch.GetTimestamp();
                        byte[]? pixels = GlassBootstrapRenderer.TryRender(scene, (int)bounds.Left, (int)bounds.Top,
                            CancellationToken.None, out var work);
                        return pixels is null ? null : new Frame(pixels, work, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                            Stopwatch.GetTimestamp());
                    });
                    Assert.NotNull(frame);
                    AssertPixels(frame.Pixels, scene, light);
                    Assert.Equal(!cold, frame.Work.PreparedResources);
                    long installedAt = 0;
                    double publicationMs = 0;
                    await Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                    {
                        long start = Stopwatch.GetTimestamp();
                        var bitmap = new WriteableBitmap(scene.PixelWidth, scene.PixelHeight, 96, 96, PixelFormats.Pbgra32, null);
                        bitmap.WritePixels(new Int32Rect(0, 0, scene.PixelWidth, scene.PixelHeight), frame.Pixels, scene.PixelWidth * 4, 0);
                        image.Source = bitmap;
                        installedAt = Stopwatch.GetTimestamp();
                        publicationMs = Stopwatch.GetElapsedTime(start, installedAt).TotalMilliseconds;
                    }, DispatcherPriority.Render);
                    samples.Add(new(cold ? "cold" : "warm", cold ? index + 1 : index - 4, light,
                        guardMs, frame.Work.CaptureMilliseconds, frame.Work.RenderMilliseconds, frame.Work.CompletionMilliseconds,
                        frame.OperationMs, Stopwatch.GetElapsedTime(frame.CompletedAt, installedAt).TotalMilliseconds,
                        publicationMs, Stopwatch.GetElapsedTime(requestedAt, installedAt).TotalMilliseconds, frame.Work.PreparedResources));
                    WriteReport(false);
                }
                finally
                {
                    if (frame is not null)
                    {
                        Array.Clear(frame.Pixels);
                        Assert.True(frame.Pixels.All(channel => channel == 0));
                    }
                }
            }
            Assert.Equal(5, samples.Count(sample => sample.Mode == "cold"));
            Assert.Equal(30, samples.Count(sample => sample.Mode == "warm"));
            exclusion.Dispose(); exclusion = null;
            Assert.True(GetWindowDisplayAffinity(owned.CaptureTargetHandle, out uint restored)); Assert.Equal(0u, restored);
            output.WriteLine($"Owned bootstrap operation (capture + GPU + completion): cold p95={Percentile(samples.Where(s => s.Mode == "cold").Select(s => s.OperationMs), .95):F2} ms; warm p95={Percentile(samples.Where(s => s.Mode == "warm").Select(s => s.OperationMs), .95):F2} ms. Guarded installation includes separate ownership overhead; no Explorer/scanout claim.");
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            image.Source = null;
            exclusion?.Dispose();
            WriteReport(failure is null && samples.Count == 35);
        }
        void WriteReport(bool completed)
        {
            object Distribution(IEnumerable<double> values)
            {
                var data = values.ToArray();
                return new { Count = data.Length, MedianMs = Percentile(data, .5), P95Ms = Percentile(data, .95) };
            }
            File.WriteAllText(Path.Combine(folder, "bootstrap-timing.json"), JsonSerializer.Serialize(new
            {
                Completed = completed, Failure = failure?.ToString(), scene.PixelWidth, scene.PixelHeight, scene.PaddingPixels,
                CapturedContent = "Fresh owned solid black/white desktop ROI only; exact padded ownership guard before every snapshot; excluded target remains visible",
                Renderer = "Production DesktopBootstrapCapture + GlassBootstrapRenderer + StudioGlassRuntime; not WGC",
                ColdScope = "GPU device/allocation reset; immutable shaders and CPU scene remain resident",
                WarmScope = "Prepared/reused GPU allocation; every observation takes a fresh desktop snapshot",
                TimingScope = "GuardedInstallationMs includes ownership preflight. OperationMs is only capture/GPU/completion work and is not end-to-end activation latency. Installation is not DWM scanout or visible-pixel proof.",
                PhysicalExplorerInitiationQualified = false, NativeDragThumbnailQualified = false, UninstrumentedActivationTargetQualified = false,
                ColdOperation = Distribution(samples.Where(s => s.Mode == "cold").Select(s => s.OperationMs)),
                WarmOperation = Distribution(samples.Where(s => s.Mode == "warm").Select(s => s.OperationMs)),
                GuardedInstallation = Distribution(samples.Select(s => s.GuardedInstallationMs)),
                Capture = Distribution(samples.Select(s => s.CaptureMs)),
                GpuSubmission = Distribution(samples.Select(s => s.RenderMs)),
                GpuCompletionAndReadback = Distribution(samples.Select(s => s.CompletionMs)),
                Publication = Distribution(samples.Select(s => s.PublicationMs)), Samples = samples
            }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        }
    }, TimeSpan.FromMinutes(10));

    private static MethodInfo AsyncPreflight()
    {
        var method = typeof(GlassCompositorCapture).GetMethod("RequireOwnedAsync", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method); return method;
    }
    private static async Task<GlassCaptureSnapshot> RequireOwned(GlassOwnedScene owned, string path, int padding) =>
        await (Task<GlassCaptureSnapshot>)AsyncPreflight().Invoke(null, [owned, nint.Zero, path, padding])!;
    private static void AssertPixels(byte[] pixels, GlassScene scene, bool light)
    {
        int malformed = 0, leaked = 0;
        for (int p = 0; p < pixels.Length; p += 4)
        {
            if (pixels[p] > pixels[p + 3] || pixels[p + 1] > pixels[p + 3] || pixels[p + 2] > pixels[p + 3]) malformed++;
            if (scene.Field[p + 2] < -1 && pixels[p + 3] != 0) leaked++;
        }
        Assert.Equal(0, malformed); Assert.Equal(0, leaked);
        foreach (var point in new[] { (184, 184), (184, 390) })
        {
            int offset = ((int)(point.Item2 * scene.Scale) * scene.PixelWidth + (int)(point.Item1 * scene.Scale)) * 4;
            Assert.Equal(255, pixels[offset + 3]);
            double source = light ? 1 : 0;
            double[] tint = [scene.Material.TintBlue, scene.Material.TintGreen, scene.Material.TintRed];
            for (int channel = 0; channel < 3; channel++)
            {
                double expected = (source * (1 - scene.Material.TintAlpha) + tint[channel] * scene.Material.TintAlpha) * 255;
                Assert.InRange(Math.Abs(pixels[offset + channel] - expected), 0, 3);
            }
        }
    }
    private static double Percentile(IEnumerable<double> values, double fraction)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return double.NaN;
        if (fraction == .5) return sorted.Length % 2 == 0 ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2];
        return sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];
    }
    private sealed record Frame(byte[] Pixels, GlassBootstrapWork Work, double OperationMs, long CompletedAt);
    private sealed record Sample(string Mode, int Index, bool Light, double GuardMs, double CaptureMs, double RenderMs,
        double CompletionMs, double OperationMs, double ValidationQueueAndPublicationMs, double PublicationMs, double GuardedInstallationMs, bool PreparedResources);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
}
