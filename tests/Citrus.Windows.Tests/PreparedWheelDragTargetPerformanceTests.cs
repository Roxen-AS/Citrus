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
using Forms = System.Windows.Forms;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Citrus.Windows.Tests;

/// <summary>Opt-in real HWND/OLE preparation and admission timings, without desktop pixels or injected input.</summary>
[Collection("Desktop")]
public sealed class PreparedWheelDragTargetPerformanceTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [WheelPerformanceFact]
    public Task Prepared_native_targets_and_real_wheel_admissions_report_separate_idle_and_input_costs() => desktop.RunAsync(async () =>
    {
        string folder = Environment.GetEnvironmentVariable("CITRUS_WHEEL_PERFORMANCE_RESULTS") is { Length: > 0 } requested && Path.IsPathFullyQualified(requested)
            ? requested : Path.Combine(AppContext.BaseDirectory, "TestResults", "wheel-performance");
        Directory.CreateDirectory(folder);
        string report = Path.Combine(folder, $"prepared-native-costs-{Environment.ProcessId}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.json");
        string file = Path.Combine(folder, "prepared-physical-payload-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "Owned physical file payload for callback admission.");
        var targets = new List<PreparedSample>();
        var admissions = new List<AdmissionSample>();
        var preparations = new List<double>();
        var placementTimes = new List<double>();
        Exception? failure = null;
        var preview = new Preview();
        var platform = new WheelDragCapturePlatform(form =>
        {
            long start = Stopwatch.GetTimestamp();
            try { return WheelDragCapturePlatform.Default.CreateHandle(form); }
            finally { preparations.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
        }, handle =>
        {
            long start = Stopwatch.GetTimestamp();
            try { WheelDragCapturePlatform.Default.Place(handle); }
            finally { placementTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
        });
        var wheel = new WheelWindow(new OperationCatalog([]), null, preview, null, dragCapturePlatform: platform);
        wheel.ApplySettings(new() { ReduceMotion = true, SoundEnabled = false });
        wheel.PrepareHiddenPresentation();
        try
        {
            for (int index = 0; index < 31; index++)
            foreach (bool preplace in index % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                WheelDragCapture? target = null;
                double preparationMs = 0, idlePlacementMs = 0;
                try
                {
                    await Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                    {
                        long start = Stopwatch.GetTimestamp();
                        target = new(wheel, platform);
                        if (preplace)
                        {
                            target.PrepareHiddenBounds();
                            idlePlacementMs = placementTimes[^1];
                        }
                        preparationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    }, DispatcherPriority.ApplicationIdle);
                    Assert.NotNull(target);
                    Assert.False(target.IsVisible);
                    Assert.Equal(0, wheel.DragCaptureHandle);
                    int before = placementTimes.Count;
                    long bindStarted = Stopwatch.GetTimestamp();
                    Assert.True(target.TryBind(static (_, _) => { }, static _ => { }, static _ => { }));
                    long bound = Stopwatch.GetTimestamp();
                    target.Start();
                    long shown = Stopwatch.GetTimestamp();
                    Assert.True(target.IsVisible);
                    Assert.Equal(2, placementTimes.Count - before);
                    targets.Add(new(index, index == 0, preparationMs, preparations[^1],
                        Span(bindStarted, bound), Span(bound, shown), Span(bindStarted, shown),
                        placementTimes[before], placementTimes[before + 1], idlePlacementMs) { HiddenPreplaced = preplace });
                }
                finally
                {
                    if (target is not null)
                    {
                        target.Release(Task.CompletedTask);
                        await target.Completion.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }
            }

            // Exercise the resident queue and actual synchronous WPF physical-file admission path.
            // Material policy stays at NoResidentServices: this test never starts desktop capture.
            for (int index = 0; index < 31; index++)
            {
                int preparedBefore = preparations.Count, previewBefore = preview.Requests;
                long queueStarted = Stopwatch.GetTimestamp();
                wheel.QueueDragCapturePreparation();
                await Dispatcher.Yield(DispatcherPriority.SystemIdle);
                long ready = Stopwatch.GetTimestamp();
                var target = Assert.IsType<WheelDragCapture>(typeof(WheelWindow).GetField("preparedCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wheel));
                Assert.Equal(preparedBefore + 1, preparations.Count);
                Assert.Equal(previewBefore, preview.Requests);
                Assert.False(target.IsVisible);
                Assert.Equal(0, wheel.DragCaptureHandle);
                Assert.True(wheel.GlassCompletion.IsCompleted);
                double idlePlacementMs = placementTimes[^1];
                int placementBefore = placementTimes.Count;
                long requestStarted = Stopwatch.GetTimestamp();
                wheel.ShowAtPointer(OperationMode.Convert);
                long pointerShown = Stopwatch.GetTimestamp();
                var surface = (Canvas)wheel.FindName("Surface");
                var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { file }),
                        DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, surface, new System.Windows.Point(184, 184)], null)!;
                args.RoutedEvent = System.Windows.DragDrop.PreviewDragEnterEvent;
                long callbackStarted = Stopwatch.GetTimestamp();
                surface.RaiseEvent(args);
                long callbackReturned = Stopwatch.GetTimestamp();
                Assert.True(args.Handled);
                Assert.Equal(target.Handle, wheel.DragCaptureHandle);
                Assert.True(target.IsVisible);
                Assert.Equal(preparedBefore + 1, preparations.Count); // No HWND/OLE creation on the input path.
                Assert.Equal(2, placementTimes.Count - placementBefore);
                admissions.Add(new(index, index == 0, Span(queueStarted, ready), preparations[^1],
                    Span(requestStarted, callbackStarted), Span(callbackStarted, callbackReturned),
                    Span(requestStarted, callbackReturned), placementTimes[placementBefore], placementTimes[placementBefore + 1], idlePlacementMs)
                {
                    ShowAtPointerMs=Span(requestStarted,pointerShown),
                    SyntheticArgumentPreparationMs=Span(pointerShown,callbackStarted)
                });
                wheel.Dismiss();
                await wheel.DragRetirementCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                // Dismiss already queued the next idle candidate. Stop it so its creation
                // belongs to the next observation, rather than silently escaping the sample.
                wheel.StopDragCapturePreparation();
            }
            WriteReport(true);
            output.WriteLine($"Prepared target report: {report}");
            output.WriteLine($"Prepared bind + full native Show warm p95: hidden placed {Percentile(targets.Where(sample => sample.HiddenPreplaced && !sample.FirstUse).Select(sample => sample.BindAndShowMs), .95):F4} ms; unplaced control {Percentile(targets.Where(sample => !sample.HiddenPreplaced && !sample.FirstUse).Select(sample => sample.BindAndShowMs), .95):F4} ms.");
            output.WriteLine($"Production admission callback warm p95: {Percentile(admissions.Skip(1).Select(sample => sample.AdmissionCallbackMs), .95):F4} ms; request through callback warm p95: {Percentile(admissions.Skip(1).Select(sample => sample.RequestThroughCallbackMs), .95):F4} ms.");
        }
        catch (Exception error) { failure = error; WriteReport(false); throw; }
        finally
        {
            wheel.ReleasePresentation();
            await wheel.DragRetirementCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            wheel.Close();
            File.Delete(file);
        }

        void WriteReport(bool completed) => File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            Completed = completed, Failure = failure?.ToString(), Environment.ProcessId, Stopwatch.Frequency,
            Scope = "Fresh real WinForms HWND/OLE target created and placed at current physical VirtualScreen bounds while hidden at ApplicationIdle on its owning WPF STA, then one-use binding and unchanged two-placement native Start. Separate series uses the resident QueueDragCapturePreparation and real WPF physical-file callback admission path.",
            Experiment = "Direct target series interleaves a fresh unplaced control and a fresh hidden-preplaced target in alternating order for each pair. Each variant has one first observation and thirty warm observations in this process. No production overrides; the production queue series always uses the current implementation.",
            Qualification = "Generated WPF drag callback over an owned physical file; native target is real and source identity uses production snapshot. No Explorer OLE drag, desktop capture, optical backing, physical input injection, or DWM scanout acceptance. Empty operation catalog; preview service returns null without I/O. Original unprepared native baseline is a separate unchanged report.",
            RequestTimingQualification = "RequestToCallback includes ShowAtPointer and synthetic DataObject/reflected DragEventArgs construction. The separate ShowAtPointer and SyntheticArgumentPreparation fields attribute these costs without subtracting them from the original elapsed measurements or gates.",
            PreparationQualification = "Direct IdlePreparationMs times hidden construction plus hidden native placement inside its ApplicationIdle callback. Production QueueToIdleReadyMs also includes dispatcher scheduling and all work before SystemIdle; NativeHandleAndOleMs and IdlePlacementMs are measured inside the production platform calls. Both admission placements remain for current desktop and z-order correction.",
            FrameBudgetMs = 16.7,
            Gates = new
            {
                PreparedBindAndFullShowWarmP95WithinBudget = Percentile(targets.Where(sample => sample.HiddenPreplaced && !sample.FirstUse).Select(sample => sample.BindAndShowMs), .95) <= 16.7,
                ProductionAdmissionCallbackWarmP95WithinBudget = Percentile(admissions.Skip(1).Select(sample => sample.AdmissionCallbackMs), .95) <= 16.7,
                ProductionRequestThroughCallbackWarmP95WithinBudget = Percentile(admissions.Skip(1).Select(sample => sample.RequestThroughCallbackMs), .95) <= 16.7
            },
            PreparedFirst = SummarizeTargets(targets.Where(sample => sample.HiddenPreplaced && sample.FirstUse)),
            PreparedWarm = SummarizeTargets(targets.Where(sample => sample.HiddenPreplaced && !sample.FirstUse)),
            UnplacedControlFirst = SummarizeTargets(targets.Where(sample => !sample.HiddenPreplaced && sample.FirstUse)),
            UnplacedControlWarm = SummarizeTargets(targets.Where(sample => !sample.HiddenPreplaced && !sample.FirstUse)),
            ProductionFirst = SummarizeAdmissions(admissions.Take(1)),
            ProductionWarm = SummarizeAdmissions(admissions.Skip(1)),
            Targets = targets, Admissions = admissions
        }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }, TimeSpan.FromMinutes(2));

    private static object SummarizeTargets(IEnumerable<PreparedSample> samples)
    {
        var values = samples.ToArray();
        return new
        {
            IdlePreparation = Distribution(values.Select(sample => sample.IdlePreparationMs)),
            NativeHandleAndOle = Distribution(values.Select(sample => sample.NativeHandleAndOleMs)),
            IdlePlacement = Distribution(values.Select(sample => sample.IdlePlacementMs)),
            OneUseBinding = Distribution(values.Select(sample => sample.BindingMs)),
            FullNativeShow = Distribution(values.Select(sample => sample.FullShowMs)),
            BindingAndFullNativeShow = Distribution(values.Select(sample => sample.BindAndShowMs)),
            PlacementBeforeShow = Distribution(values.Select(sample => sample.PlacementBeforeShowMs)),
            PlacementAfterShow = Distribution(values.Select(sample => sample.PlacementAfterShowMs))
        };
    }

    private static object SummarizeAdmissions(IEnumerable<AdmissionSample> samples)
    {
        var values = samples.ToArray();
        return new
        {
            QueueToIdleReady = Distribution(values.Select(sample => sample.QueueToIdleReadyMs)),
            NativeHandleAndOle = Distribution(values.Select(sample => sample.NativeHandleAndOleMs)),
            IdlePlacement = Distribution(values.Select(sample => sample.IdlePlacementMs)),
            ShowAtPointer = Distribution(values.Select(sample => sample.ShowAtPointerMs)),
            SyntheticArgumentPreparation = Distribution(values.Select(sample => sample.SyntheticArgumentPreparationMs)),
            RequestToCallback = Distribution(values.Select(sample => sample.RequestToCallbackMs)),
            AdmissionCallback = Distribution(values.Select(sample => sample.AdmissionCallbackMs)),
            RequestThroughCallback = Distribution(values.Select(sample => sample.RequestThroughCallbackMs)),
            PlacementBeforeShow = Distribution(values.Select(sample => sample.PlacementBeforeShowMs)),
            PlacementAfterShow = Distribution(values.Select(sample => sample.PlacementAfterShowMs))
        };
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
    private sealed record PreparedSample(int Index, bool FirstUse, double IdlePreparationMs, double NativeHandleAndOleMs,
        double BindingMs, double FullShowMs, double BindAndShowMs, double PlacementBeforeShowMs, double PlacementAfterShowMs, double IdlePlacementMs)
    {
        public bool HiddenPreplaced { get; init; }
    }
    private sealed record AdmissionSample(int Index, bool FirstUse, double QueueToIdleReadyMs, double NativeHandleAndOleMs,
        double RequestToCallbackMs, double AdmissionCallbackMs, double RequestThroughCallbackMs, double PlacementBeforeShowMs, double PlacementAfterShowMs, double IdlePlacementMs)
    {
        public double ShowAtPointerMs {get;init;}
        public double SyntheticArgumentPreparationMs {get;init;}
    }
    private sealed class Preview : IFilePreviewService
    {
        public int Requests { get; private set; }
        public Task<ImageSource?> LoadAsync(string path, int pixelLimit, CancellationToken cancellationToken)
        { Requests++; return Task.FromResult<ImageSource?>(null); }
    }
}
