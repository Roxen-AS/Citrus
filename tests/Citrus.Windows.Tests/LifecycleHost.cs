using System.IO;
using System.Windows.Interop;

namespace Citrus.Windows.Tests;

/// <summary>Explicit executable entry point used only by the isolated lifecycle test process.</summary>
internal static class LifecycleHost
{
    [STAThread]
    public static int Main(string[] args)
    {
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
        app.ResidentReady += () =>
        {
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
                            case "move":
                                _ = app.SubmitAsync(new(FileActions.Operations.Single(operation => operation.Id == "action.move"), [Path.Combine(args[2], "move-source.txt")]));
                                return;
                            case "quit": Console.WriteLine("QUIT_REQUESTED"); await app.QuitAsync(); return;
                        }
                        Console.WriteLine($"STATE:home={app.HomeVisible};wheel={app.WheelVisible}");
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
