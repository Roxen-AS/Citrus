using System.IO;
using System.Windows.Interop;

namespace Citrus.Windows.Tests;

/// <summary>Explicit executable entry point used only by the isolated lifecycle test process.</summary>
internal static class LifecycleHost
{
    [STAThread]
    public static int Main(string[] args)
    {
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
            Console.WriteLine("READY:" + new WindowInteropHelper(app.MainWindow).EnsureHandle());
            _ = Task.Run(async () =>
            {
                while (await Console.In.ReadLineAsync() is { } command)
                {
                    await (await app.Dispatcher.InvokeAsync(async () =>
                    {
                        switch (command)
                        {
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
                                var preparationWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                                preparationWatch.Tick += (_, _) =>
                                {
                                    var dialog = app.Windows.OfType<ToolOptionsWindow>().FirstOrDefault();
                                    if (dialog is null || !dialog.IsVisible) return;
                                    preparationWatch.Stop();
                                    // Hold at real pre-enqueue confirmation, suppress the initial checking toast,
                                    // then accept the same submission. Completion must not reacquire a window.
                                    if (command == "prepare-pause") app.SetGestures(false, persist: false);
                                    else app.Windows.OfType<JobToastWindow>().Single().Close();
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
        return result;
    }
}
