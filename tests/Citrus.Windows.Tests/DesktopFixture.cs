using System.Windows.Threading;

namespace Citrus.Windows.Tests;

[CollectionDefinition("Desktop", DisableParallelization = true)]
public sealed class DesktopCollection : ICollectionFixture<DesktopFixture>;

/// <summary>One real WPF application/STA dispatcher, without invoking product startup or global hooks.</summary>
public sealed class DesktopFixture : IDisposable
{
    private readonly Thread thread;
    private readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int SuppressedStartupCallbacks { get; private set; }
    public DesktopFixture()
    {
        thread = new Thread(() =>
        {
            try
            {
                var app = new ResourceOnlyApp(() =>
                {
                    SuppressedStartupCallbacks++;
                    ready.SetResult(Dispatcher.CurrentDispatcher);
                });
                app.InitializeComponent();
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                Dispatcher.Run();
            }
            catch (Exception error) { ready.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
    public async Task RunAsync(Func<Task> action)
    {
        var dispatcher = await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await (await dispatcher.InvokeAsync(action)).WaitAsync(TimeSpan.FromSeconds(30));
    }
    public void Dispose()
    {
        if (ready.Task.IsCompletedSuccessfully) ready.Task.Result.BeginInvokeShutdown(DispatcherPriority.Send);
        if (!thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("Desktop dispatcher did not stop.");
    }

    private sealed class ResourceOnlyApp(Action startupSuppressed) : App
    {
        // Application's constructor queues OnStartup even when only Dispatcher.Run is used.
        // Load the real App.xaml, but never start the resident, its hooks, or presentation services.
        protected override void OnStartup(System.Windows.StartupEventArgs e) => startupSuppressed();
    }
}
