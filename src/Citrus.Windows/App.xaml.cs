using System.ComponentModel;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Core;
using Forms = System.Windows.Forms;

namespace Citrus.Windows;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Forms.ToolStripMenuItem? pauseItem;
    private GestureMonitor? monitor;
    private WheelWindow? wheel;
    private MainWindow? home;
    private JobQueue? queue;
    private SubmissionService? submissions;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<ConversionJob> jobs = [];
    private readonly HashSet<ConversionJob> notified = [];
    private bool preparing;
    public bool IsQuitting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\Citrus-" + Environment.UserName, out ownsInstance);
        if (!ownsInstance) { Shutdown(); return; }
        home = new(this);
        MainWindow = home;
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Resources/Citrus.ico")).Stream;
        trayIcon = new System.Drawing.Icon(iconStream);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Citrus · local file copies", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Citrus", null, (_, _) => OpenHome());
        pauseItem = new Forms.ToolStripMenuItem("Pause drag gestures") { CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) => SetGestures(!pauseItem.Checked);
        menu.Items.Add(pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Citrus", null, async (_, _) => await QuitAsync());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => OpenHome();
        home.Show();
        try
        {
            var registry = await EngineRegistry.Create(new OutputWriter(), Path.Combine(AppContext.BaseDirectory, "media"), lifetime.Token);
            if (IsQuitting) return;
            queue = new(registry.Engines);
            queue.JobChanged += JobChanged;
            submissions = new(queue, registry.Inspector.InspectAsync);
            var catalog = new OperationCatalog(registry.Engines);
            wheel = new(catalog);
            wheel.OperationDropped += SubmitDrop;
            home.Configure(catalog, registry.Warnings);
            try
            {
                monitor = new(Dispatcher);
                monitor.ModeRequested += wheel.ShowAtPointer;
                monitor.EscapePressed += wheel.Dismiss;
                monitor.DragReleased += wheel.ObserveGlobalRelease;
                SetGestures(pauseItem?.Checked != true);
            }
            catch (Win32Exception)
            {
                home.Report("Gesture observation could not start. You can still add files here. Restart Citrus to retry.");
                SetGestures(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { home.Report("Citrus could not initialize its engines. Restart or reinstall Citrus. " + error.Message); }
    }

    internal void OpenHome() { home?.Show(); home?.Activate(); }

    internal void SetGestures(bool enabled)
    {
        if (monitor?.Enabled != enabled) monitor?.SetEnabled(enabled);
        if (!enabled) wheel?.Dismiss();
        if (pauseItem is not null && pauseItem.Checked == enabled) pauseItem.Checked = !enabled;
        home?.SetGestureCheck(enabled);
    }

    private async void SubmitDrop(OperationDrop drop) => await SubmitAsync(drop);

    internal Task RetryAsync(ConversionJob job, string? destination) => SubmitAsync(new(job.Request.Operation, job.Request.Inputs), job.Request.Options, destination);

    // Composition seam for Task 7: all conversion entry points pass through this service;
    // actions/settings can compose beside it without duplicating the queue or options flow.
    internal async Task SubmitAsync(OperationDrop drop, OperationOptions? options = null, string? destination = null)
    {
        if (IsQuitting || submissions is null || home is null) return;
        if (preparing) { home.Report("Finish the current options dialog before starting another submission."); return; }
        preparing = true;
        home.SetBusy(true);
        OpenHome();
        home.Report("Checking inputs…");
        try
        {
            var submitted = await submissions.SubmitAsync(drop.Operation, drop.Paths, ConfirmAsync, options, destination, lifetime.Token);
            jobs.AddRange(submitted);
            home.AddJobs(submitted);
            home.Report(submitted.Count == 0 ? "Canceled. No work was submitted." : $"Submitted {submitted.Count} job(s). Each ordinary input has its own result.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { home.Report("Could not submit: " + error.Message); }
        finally { preparing = false; home.SetBusy(false); }
    }

    private Task<SubmissionChoice?> ConfirmAsync(SubmissionDraft draft, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new ToolOptionsWindow(draft) { Owner = home };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Choice : null);
    }

    private void JobChanged(ConversionJob job)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (IsQuitting) return;
            home?.RefreshJobs();
            if (job.State is ConversionJobState.Succeeded or ConversionJobState.Failed && notified.Add(job))
                tray?.ShowBalloonTip(3000, "Citrus", job.State == ConversionJobState.Succeeded ? "Your copies are ready. Open Citrus to reveal them." : "An item failed. Open Citrus for details and retry options.",
                    job.State == ConversionJobState.Succeeded ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
        }));
    }

    internal async Task QuitAsync()
    {
        if (IsQuitting) return;
        IsQuitting = true;
        lifetime.Cancel();
        monitor?.Dispose();
        wheel?.Dismiss();
        if (tray is not null) tray.Visible = false;
        foreach (var job in jobs) job.Cancel();
        await Task.WhenAll(jobs.Select(job => job.Completion));
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lifetime.Cancel();
        monitor?.Dispose();
        if (queue is not null) queue.JobChanged -= JobChanged;
        tray?.Dispose();
        trayIcon?.Dispose();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        lifetime.Dispose();
        base.OnExit(e);
    }
}
