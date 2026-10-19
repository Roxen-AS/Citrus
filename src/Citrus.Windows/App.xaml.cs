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
    private readonly List<ConversionJob> jobs = [];
    public bool IsQuitting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\Citrus-" + Environment.UserName, out ownsInstance);
        if (!ownsInstance) { Shutdown(); return; }
        var engine = new ImageEngine();
        queue = new([engine]);
        queue.JobChanged += JobChanged;
        wheel = new(new OperationCatalog([engine]));
        wheel.OperationDropped += Submit;
        home = new(this, engine.Operations);
        MainWindow = home;
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Resources/Citrus.ico")).Stream;
        trayIcon = new System.Drawing.Icon(iconStream);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Citrus · local image copies", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Citrus", null, (_, _) => OpenHome());
        pauseItem = new Forms.ToolStripMenuItem("Pause drag gestures") { CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) => SetGestures(!pauseItem.Checked);
        menu.Items.Add(pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Citrus", null, async (_, _) => await QuitAsync());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => OpenHome();
        try
        {
            monitor = new(Dispatcher);
            monitor.ModeRequested += wheel.ShowAtPointer;
            monitor.EscapePressed += wheel.Dismiss;
            monitor.DragReleased += wheel.ObserveGlobalRelease;
        }
        catch (Win32Exception)
        {
            home.Report("Gesture observation could not start. You can still choose an image here. Restart Citrus to retry.");
            SetGestures(false);
        }
        home.Show();
    }

    private void OpenHome() { home?.Show(); home?.Activate(); }

    internal void SetGestures(bool enabled)
    {
        if (monitor?.Enabled != enabled) monitor?.SetEnabled(enabled);
        if (!enabled) wheel?.Dismiss();
        if (pauseItem is not null && pauseItem.Checked == enabled) pauseItem.Checked = !enabled;
        home?.SetGestureCheck(enabled);
    }

    internal void Submit(OperationDrop drop)
    {
        if (IsQuitting || queue is null) return;
        var options = new OperationOptions();
        if (drop.Operation.NeedsOptions)
        {
            var dialog = new ToolOptionsWindow(drop.Operation);
            if (dialog.ShowDialog() != true) return;
            options = dialog.Options;
        }
        // Each ordinary file is an independent core request. Later batch UI can aggregate these outcomes.
        foreach (var path in drop.Paths)
            jobs.Add(queue.Enqueue(new(drop.Operation, [path], options, null)));
    }

    private void JobChanged(ConversionJob job)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (IsQuitting) return;
            var messages = jobs.TakeLast(8).Select(item => $"{Path.GetFileName(item.Request.Inputs[0])} · {item.Request.Operation.Label}\n" +
                (item.State == ConversionJobState.Succeeded ? "Saved " + Path.GetFileName(item.Outputs.FirstOrDefault()) :
                 item.State == ConversionJobState.Failed ? "Could not create a copy. " + item.Error?.Message : item.Progress?.Message ?? item.State.ToString()));
            home?.Report(string.Join("\n\n", messages));
            if (job.State is ConversionJobState.Succeeded or ConversionJobState.Failed)
                tray?.ShowBalloonTip(3000, "Citrus", job.State == ConversionJobState.Succeeded ? "Your image copy is ready beside its original." : "A copy could not be created. Open Citrus for details.",
                    job.State == ConversionJobState.Succeeded ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
        }));
    }

    private async Task QuitAsync()
    {
        if (IsQuitting) return;
        IsQuitting = true;
        monitor?.Dispose();
        wheel?.Dismiss();
        if (tray is not null) tray.Visible = false;
        foreach (var job in jobs) job.Cancel();
        await Task.WhenAll(jobs.Select(job => job.Completion));
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        monitor?.Dispose();
        tray?.Dispose();
        trayIcon?.Dispose();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
