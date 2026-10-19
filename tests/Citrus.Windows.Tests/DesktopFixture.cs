using System.Windows.Threading;

namespace Citrus.Windows.Tests;

[CollectionDefinition("Desktop", DisableParallelization = true)]
public sealed class DesktopCollection : ICollectionFixture<DesktopFixture>;

/// <summary>One real WPF application/STA dispatcher, without invoking product startup or global hooks.</summary>
public sealed class DesktopFixture : IDisposable
{
    private readonly Thread thread;
    private readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DesktopDispatcherGuard guard = new();
    private readonly TaskCompletionSource<Exception> fatalExit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int disposalRequested;
    internal int SuppressedStartupCallbacks { get; private set; }
    public DesktopFixture()
    {
        thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                var app = new ResourceOnlyApp(() =>
                {
                    SuppressedStartupCallbacks++;
                    ready.SetResult(Dispatcher.CurrentDispatcher);
                });
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                Dispatcher.Run();
                bool expected = Volatile.Read(ref disposalRequested) != 0;
                DesktopStageTrace.RecordDispatcherExit(Thread.CurrentThread, dispatcher, normalReturn: true, expected, error: null);
                if (!expected)
                {
                    var error = new InvalidOperationException("Desktop dispatcher returned before fixture disposal.");
                    ready.TrySetException(error);
                    fatalExit.TrySetResult(error);
                }
            }
            catch (Exception error)
            {
                try { DesktopStageTrace.RecordDispatcherExit(Thread.CurrentThread, dispatcher, normalReturn: false,
                    Volatile.Read(ref disposalRequested) != 0, error); }
                catch (Exception evidenceError) { error.Data["DispatcherEvidenceWriteFailure"] = evidenceError.ToString(); }
                // Readiness may already be complete. Preserve the original fatal error for the active action too.
                ready.TrySetException(error);
                fatalExit.TrySetResult(error);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
    public async Task RunAsync(Func<Task> action)
    {
        var dispatcher = await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await guard.RunAsync(dispatcher, action, TimeSpan.FromSeconds(30), fatalExit.Task);
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref disposalRequested, 1);
        if (ready.Task.IsCompletedSuccessfully) ready.Task.Result.BeginInvokeShutdown(DispatcherPriority.Send);
        if (!thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("Desktop dispatcher did not stop.");
    }

    private sealed class ResourceOnlyApp : App
    {
        private readonly Action startupSuppressed;

        public ResourceOnlyApp(Action startupSuppressed)
        {
            this.startupSuppressed = startupSuppressed;
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            Resources = new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Citrus.Windows;component/CitrusResources.xaml", UriKind.Absolute)
            };
        }

        // Application's constructor queues OnStartup even when only Dispatcher.Run is used.
        // The dispatcher starts after the shared production resources have loaded, so readiness
        // observes both resources and this callback without starting resident services or hooks.
        protected override void OnStartup(System.Windows.StartupEventArgs e) => startupSuppressed();
    }
}
