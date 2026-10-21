using System.IO;
using System.Windows.Interop;

namespace Citrus.Windows.Tests;

/// <summary>Explicit executable entry point used only by the isolated lifecycle test process.</summary>
internal static class LifecycleHost
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--wheel-performance-proof")
        {
            using var desktop = new DesktopFixture();
            try
            {
                new NativeWheelPerformanceMeasurementsTests(desktop, new NativeProofOutput())
                    .Fresh_target_and_generated_gpu_measurements_report_separate_costs().GetAwaiter().GetResult();
                Console.WriteLine("WHEEL_PERFORMANCE_COSTS:PASS"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--bootstrap-proof")
        {
            using var desktop = new DesktopFixture();
            try
            {
                new NativeBootstrapProofTests(desktop, new NativeProofOutput())
                    .Five_cold_and_thirty_warm_snapshots_qualify_owned_bootstrap_pixels_and_report_phases().GetAwaiter().GetResult();
                Console.WriteLine("BOOTSTRAP_PROOF:PASS"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--backing-startup-proof")
        {
            using var desktop = new DesktopFixture();
            try
            {
                new NativeBackingStartupTests(desktop, new NativeProofOutput())
                    .Five_cold_and_thirty_warm_backing_activations_report_end_to_end_phases().GetAwaiter().GetResult();
                Console.WriteLine("BACKING_STARTUP_PROOF:PASS"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--cancel-click-proof")
        {
            // Use the manifested apphost and isolate the unknown-source recovery proof.
            using var desktop = new DesktopFixture();
            try
            {
                new NativeWheelCancellationTests(desktop, new NativeProofOutput())
                    .Cancelled_unknown_source_releases_the_desktop_target_and_next_native_left_click_reaches_owned_window().GetAwaiter().GetResult();
                Console.WriteLine("CANCEL_CLICK_PROOF:PASS"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--live-wheel-proof")
        {
            // Keep the product PerMonitorV2 apphost while isolating wheel optical evidence.
            using var desktop = new DesktopFixture();
            try
            {
                new NativeLiveWheelGlassTests(desktop, new NativeProofOutput())
                    .Admitted_wheel_refracts_owned_desktop_with_both_windows_excluded_and_policy_teardown_restores_them().GetAwaiter().GetResult();
                Console.WriteLine("LIVE_WHEEL_PROOF:PASS"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--studio-chrome-proof")
        {
            using var desktop = new DesktopFixture();
            try
            {
                new NativeStudioChromeTests(desktop, new NativeProofOutput())
                    .Settings_chrome_refracts_owned_desktop_and_releases_capture_when_hidden_or_minimized().GetAwaiter().GetResult();
                Console.WriteLine("STUDIO_CHROME_PROOF:PASS"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--liquid-glass-proof")
        {
            // The native apphost carries the same PerMonitorV2 manifest as Citrus.
            // vstest's unrelated host cannot establish that process-level contract.
            using var desktop = new DesktopFixture();
            var output = new NativeProofOutput();
            try
            {
                new LiquidGlassCaptureProbeTests(desktop, output)
                    .Layered_wheel_capture_exclusion_reveals_changing_owned_background_without_feedback().GetAwaiter().GetResult();
                Console.WriteLine("SOURCE_PROOF:PASS");
                new NativeLiveWheelGlassTests(desktop, output)
                    .Admitted_wheel_refracts_owned_desktop_with_both_windows_excluded_and_policy_teardown_restores_them().GetAwaiter().GetResult();
                Console.WriteLine("LIVE_WHEEL_PROOF:PASS");
                Console.WriteLine("STUDIO_PROOF:PASS");
                new NativeStudioChromeTests(desktop, output)
                    .Settings_chrome_refracts_owned_desktop_and_releases_capture_when_hidden_or_minimized().GetAwaiter().GetResult();
                Console.WriteLine("STUDIO_CHROME_PROOF:PASS");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--glass-projection-smoke") return GlassProjectionSmoke.Run();
        if (args.Length == 1 && args[0] == "--avif-handle-holder") return AvifHandleInheritanceHost.Hold();
        if (args.Length == 3 && args[0] == "--avif-handle-reader") return AvifHandleInheritanceHost.Read(args[1], args[2]);
        if (args.Length < 3 || args[0] != "--resident-test") return 2;
        FileActions? actions = null;
        if (args.Contains("--owned-move"))
        {
            actions = new FileActions(pickDestination: () => Path.Combine(args[2], "move-target"))
            {
                BeforeMovePerform = () =>
                {
                    using var release = EventWaitHandle.OpenExisting(@"Local\Citrus-test-move-" + args[1]);
                    Console.WriteLine("MOVE_READY");
                    if (!release.WaitOne(TimeSpan.FromSeconds(30))) throw new TimeoutException("The owned native move test was not released.");
                }
            };
        }
        var app = new App(new ResidentOptions(args[2], args[1], ObserveGestures: false, Background: args.Contains("--background"), NativeActions: actions));
        app.InitializeComponent();
        // Opt-in test diagnostics retain actual resident references, without changing product APIs.
        GlassLifetimeObservation? glassLifetime = null;
        // Surface product startup errors from a hidden Home instead of waiting 45 seconds for READY.
        var startupWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        startupWatch.Tick += (_, _) =>
        {
            if (app.MainWindow is MainWindow home && home.FindName("Status") is System.Windows.Controls.TextBlock status &&
                status.Text.StartsWith("Citrus could not initialize.", StringComparison.Ordinal))
            {
                startupWatch.Stop();
                Console.WriteLine("STARTUP_FAILED:" + status.Text);
            }
        };
        startupWatch.Start();
        app.ResidentReady += () =>
        {
            startupWatch.Stop();
            if (args.Contains("--glass-lifetime")) glassLifetime = new(app);
            Console.WriteLine("READY:" + new WindowInteropHelper(app.MainWindow).EnsureHandle());
            _ = Task.Run(async () =>
            {
                while (await Console.In.ReadLineAsync() is { } command)
                {
                    await (await app.Dispatcher.InvokeAsync(async () =>
                    {
                        switch (command)
                        {
                            case "glass-lifetime" when glassLifetime is not null:
                                Console.WriteLine("GLASS_READY:" + glassLifetime.Prepare(Path.Combine(args[2], "glass.txt")));
                                return;
                            case "open": app.OpenHome(); break;
                            case "close": app.CloseHome(); break;
                            case "pause": app.ShowWheelForLifecycleTest(); app.SetGestures(false, persist: false); break;
                            case "wheel-submit":
                                await app.SubmitAsync(new(new Citrus.Conversion.SubtitleEngine().Operations.First(o => o.OutputExtension == ".vtt"), [Path.Combine(args[2], "quiet.srt")]), origin: Citrus.Core.SubmissionOrigin.Wheel);
                                await Task.WhenAll(((System.Windows.Controls.ItemsControl)((MainWindow)app.MainWindow).FindName("JobList")).Items.Cast<JobViewModel>().Select(model => model.Job.Completion));
                                break;
                            case "wheel-options":
                                bool? owned = null;
                                var optionsWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                                optionsWatch.Tick += (_, _) =>
                                {
                                    var dialog = app.Windows.OfType<ToolOptionsWindow>().FirstOrDefault();
                                    if (dialog is null || !dialog.IsVisible) return;
                                    owned = dialog.Owner is not null;
                                    optionsWatch.Stop();
                                    dialog.Close();
                                };
                                optionsWatch.Start();
                                try
                                {
                                    await app.SubmitAsync(new(new Citrus.Conversion.ArchiveEngine().Operations.First(o => o.Id == "archive.zip"), [Path.Combine(args[2], "quiet.srt")]), origin: Citrus.Core.SubmissionOrigin.Wheel);
                                }
                                finally { optionsWatch.Stop(); }
                                Console.WriteLine($"OPTIONS:owned={owned};home={app.HomeVisible}");
                                return;
                            case "prepare-dismiss":
                            case "prepare-pause":
                            case "prepare-observe":
                                var preparationWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                                preparationWatch.Tick += (_, _) =>
                                {
                                    var dialog = app.Windows.OfType<ToolOptionsWindow>().FirstOrDefault();
                                    if (dialog is null || !dialog.IsVisible) return;
                                    preparationWatch.Stop();
                                    // Hold at real pre-enqueue confirmation, suppress the initial checking toast,
                                    // then accept the same submission. Completion must not reacquire a window.
                                    if (command == "prepare-pause") app.SetGestures(false, persist: false);
                                    else if (command == "prepare-dismiss") app.Windows.OfType<JobToastWindow>().Single().Close();
                                    else Console.WriteLine("PREPARATION_TOAST:" + app.Windows.OfType<JobToastWindow>().Any(window => window.IsVisible));
                                    ((System.Windows.Controls.Button)dialog.FindName("CreateButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                };
                                preparationWatch.Start();
                                try
                                {
                                    await app.SubmitAsync(new(new Citrus.Conversion.ArchiveEngine().Operations.First(o => o.Id == "archive.zip"), [Path.Combine(args[2], "quiet.srt")]), origin: Citrus.Core.SubmissionOrigin.Wheel);
                                    await Task.WhenAll(((System.Windows.Controls.ItemsControl)((MainWindow)app.MainWindow).FindName("JobList")).Items.Cast<JobViewModel>().Select(model => model.Job.Completion));
                                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                                }
                                finally { preparationWatch.Stop(); }
                                break;
                            case "wheel-error":
                                await app.SubmitAsync(new(new Citrus.Conversion.SubtitleEngine().Operations.First(o => o.OutputExtension == ".vtt"), [Path.Combine(args[2], "missing.srt")]), origin: Citrus.Core.SubmissionOrigin.Wheel);
                                await Task.WhenAll(((System.Windows.Controls.ItemsControl)((MainWindow)app.MainWindow).FindName("JobList")).Items.Cast<JobViewModel>().Select(model => model.Job.Completion));
                                break;
                            case "prepare-overlap":
                                Console.WriteLine("PREPARATION_SURVIVED:" + await QuietPreparationObservation.Run(app, args[2]));
                                return;
                            case "move":
                                _ = app.SubmitAsync(new(FileActions.Operations.Single(operation => operation.Id == "action.move"), [Path.Combine(args[2], "move-source.txt")]));
                                return;
                            case "quit": Console.WriteLine("QUIT_REQUESTED"); await app.QuitAsync(); return;
                        }
                        var home = (MainWindow)app.MainWindow;
                        var jobs = ((System.Windows.Controls.ItemsControl)home.FindName("JobList")).Items.Cast<JobViewModel>().ToArray();
                        int errors = jobs.Count(model => model.Job.State == Citrus.Core.ConversionJobState.Failed) + ((System.Windows.Controls.ItemsControl)home.FindName("SubmissionFailures")).Items.Count;
                        Console.WriteLine($"STATE:home={app.HomeVisible};wheel={app.WheelVisible};succeeded={jobs.Count(model => model.Job.State == Citrus.Core.ConversionJobState.Succeeded)};errors={errors};toast={app.Windows.OfType<JobToastWindow>().Any(window => window.IsVisible)}");
                    }));
                    if (command == "quit") return;
                }
                await (await app.Dispatcher.InvokeAsync(app.QuitAsync));
            });
        };
        int result = app.Run();
        Console.WriteLine("DISPOSED:" + app.ResourcesDisposed);
        if (glassLifetime is not null) Console.WriteLine("GLASS_DISPOSED:" + glassLifetime.AfterQuit());
        return result;
    }

    private sealed class NativeProofOutput : Xunit.Abstractions.ITestOutputHelper
    {
        public void WriteLine(string message) => Console.WriteLine(message);
        public void WriteLine(string format, params object[] args) => Console.WriteLine(format, args);
    }
}
