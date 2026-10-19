using System.ComponentModel;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Core;
using Forms = System.Windows.Forms;

namespace Citrus.Windows;

// Isolated child-process tests exercise the resident without installing global input hooks or changing user settings.
internal sealed record ResidentOptions(string? SettingsRoot = null, string? InstanceSuffix = null, bool ObserveGestures = true, bool Background = false, FileActions? NativeActions = null);

public partial class App : Application
{
    private readonly ResidentOptions options;
    private SingleInstance? instance;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Forms.ToolStripMenuItem? pauseItem;
    private GestureMonitor? monitor;
    private WheelWindow? wheel;
    private MainWindow? home;
    private ShelfWindow? shelf;
    private SettingsWindow? settingsWindow;
    private ThemeManager? themes;
    private FeedbackAudio? feedbackAudio;
    private readonly SettingsStore settingsStore;
    private CitrusSettings settings = new();
    private Task<EngineRegistry>? discovery;
    private JobQueue? queue;
    private SubmissionService? submissions;
    private FileActions? actions;
    private Task activeAction = Task.CompletedTask;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<ConversionJob> jobs = [];
    private readonly HashSet<ConversionJob> notified = [];
    private bool preparing, updatingGestures;
    public bool IsQuitting { get; private set; }
    internal event Action? ResidentReady;
    internal bool HomeVisible => home?.IsVisible == true;
    internal bool WheelVisible => wheel?.IsVisible == true;
    internal bool ResourcesDisposed { get; private set; }
    internal CitrusSettings Settings => settings;
    public App() : this(new ResidentOptions()) { }
    internal App(ResidentOptions options) { this.options = options; settingsStore = new(options.SettingsRoot); }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            instance = new(options.InstanceSuffix);
            if (!instance.IsOwner)
            {
                // Sign-in activation stays quiet. A normal second launch opens the existing Home.
                if (!options.Background && !e.Args.Contains("--background", StringComparer.Ordinal)) await instance.ActivateAsync();
                await instance.DisposeAsync(); instance = null;
                Shutdown(); return;
            }
            instance.Listen(() => Dispatcher.BeginInvoke(new Action(OpenHome)));
            settings = settingsStore.Load();
            themes = new(this);
            themes.Apply(settings.Theme, settings.ReduceMotion);
            feedbackAudio = new() { Enabled = settings.SoundEnabled };
            home = new(this);
            MainWindow = home;
            actions = options.NativeActions ?? new(floatPaths: FloatPaths, cancellationToken: lifetime.Token);
            using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Resources/Citrus.ico")).Stream;
            trayIcon = new System.Drawing.Icon(iconStream);
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Citrus · local files", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Citrus", null, (_, _) => OpenHome());
            pauseItem = new Forms.ToolStripMenuItem("Pause drag gestures") { CheckOnClick = true };
            pauseItem.CheckedChanged += (_, _) => { if (!updatingGestures) SetGestures(!pauseItem.Checked); };
            menu.Items.Add(pauseItem);
            menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit Citrus", null, async (_, _) => await QuitAsync());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => OpenHome();
            if (!options.Background && !e.Args.Contains("--background", StringComparer.Ordinal)) home.Show();
            SetGestures(settings.GesturesEnabled, persist: false);
            discovery = EngineRegistry.Create(new OutputWriter(), Path.Combine(AppContext.BaseDirectory, "media"), lifetime.Token);
            var registry = await discovery;
            if (IsQuitting) return;
            queue = new(registry.Engines);
            queue.JobChanged += JobChanged;
            submissions = new(queue, registry.Inspector.InspectAsync);
            // Native actions participate in discovery only; the worker queue owns conversion engines exclusively.
            var catalog = new OperationCatalog(registry.Engines.Concat([new NativeActionCatalog()]));
            wheel = new(catalog, feedbackAudio);
            wheel.ApplySettings(settings);
            wheel.OperationDropped += SubmitDrop;
            home.Configure(catalog, registry.Warnings);
            try
            {
                if (options.ObserveGestures)
                {
                    monitor = new(Dispatcher);
                    monitor.ModeRequested += mode => wheel.ShowAtPointer(settings.Remap(mode));
                    monitor.EscapePressed += wheel.Dismiss;
                    monitor.DragReleased += wheel.ObserveGlobalRelease;
                }
                SetGestures(settings.GesturesEnabled, persist: false);
            }
            catch (Win32Exception)
            {
                home.Report("Gesture observation could not start. You can still add files here. Restart Citrus to retry.");
                SetGestures(false, persist: false);
            }
            if (settingsStore.LoadWarning is { } warning) home.Report(warning);
            ResidentReady?.Invoke();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (home is not null) home.Report("Citrus could not initialize. Restart or reinstall Citrus. " + error.Message);
            else { MessageBox.Show("Citrus could not start: " + error.Message, "Citrus"); await QuitAsync(); }
        }
    }

    internal void OpenHome()
    {
        if (IsQuitting) return;
        home?.Show();
        if (home?.WindowState == WindowState.Minimized) home.WindowState = WindowState.Normal;
        home?.Activate();
    }
    internal void CloseHome() => home?.Close();
    internal void ShowWheelForLifecycleTest() => wheel?.ShowAtPointer(OperationMode.Actions);
    internal void OpenSettings()
    {
        if (IsQuitting) return;
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        OpenHome();
        settingsWindow = new(settings, ApplySettingsAsync) { Owner = home };
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
    private void FloatPaths(IReadOnlyList<string> paths)
    {
        if (shelf is null)
        {
            shelf = new();
            shelf.Closed += (_, _) => shelf = null;
        }
        shelf.AddPaths(paths); shelf.Show(); shelf.Activate();
    }
    internal Task ApplySettingsAsync(CitrusSettings updated)
    {
        if (IsQuitting) throw new InvalidOperationException("Citrus is quitting.");
        updated.Validate();
        // Only an explicit preference change writes HKCU Run. Test hosts never use the real startup key.
        var startup = new StartupRegistration();
        bool startupChanged = updated.LaunchAtSignIn != settings.LaunchAtSignIn;
        if (startupChanged && options.SettingsRoot is null) startup.Apply(updated.LaunchAtSignIn);
        try { settingsStore.Save(updated); }
        catch
        {
            if (startupChanged && options.SettingsRoot is null) startup.Apply(settings.LaunchAtSignIn);
            throw;
        }
        settings = updated;
        themes?.Apply(settings.Theme, settings.ReduceMotion);
        wheel?.ApplySettings(settings);
        if (feedbackAudio is not null) feedbackAudio.Enabled = settings.SoundEnabled;
        SetGestures(settings.GesturesEnabled, persist: false);
        home?.UpdateGestureGuide(settings);
        return Task.CompletedTask;
    }
    internal void SetGestures(bool enabled, bool persist = true)
    {
        if (updatingGestures || IsQuitting) return;
        updatingGestures = true;
        try
        {
            if (persist && settings.GesturesEnabled != enabled)
            {
                var updated = settings with { GesturesEnabled = enabled };
                try { settingsStore.Save(updated); settings = updated; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { home?.Report("Gesture setting could not be saved: " + error.Message); }
            }
            if (monitor?.Enabled != enabled) monitor?.SetEnabled(enabled);
            if (!enabled) wheel?.Dismiss();
            if (pauseItem is not null) pauseItem.Checked = !enabled;
            home?.SetGestureCheck(enabled);
            home?.UpdateGestureGuide(settings);
        }
        finally { updatingGestures = false; }
    }
    private async void SubmitDrop(OperationDrop drop) => await SubmitAsync(drop);
    internal Task RetryAsync(ConversionJob job, string? destination) => SubmitAsync(new(job.Request.Operation, job.Request.Inputs), job.Request.Options, destination);
    internal async Task SubmitAsync(OperationDrop drop, OperationOptions? options = null, string? destination = null)
    {
        if (IsQuitting || submissions is null || home is null) return;
        if (preparing) { home.Report("Finish the current action or options dialog before starting another submission."); return; }
        preparing = true;
        home.SetBusy(true);
        OpenHome();
        home.Report("Checking inputs…");
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        activeAction = completed.Task; // Installed before a destination dialog can pump nested Quit messages.
        try
        {
            if (drop.Operation.Mode == OperationMode.Actions)
            {
                await actions!.ExecuteAsync(drop.Operation.Id, drop.Paths);
                home.RefreshSelection();
                home.Report(drop.Operation.Id == "action.move" ? "Moved to: " + string.Join("; ", actions.LastMoveDestinations) :
                    $"{drop.Operation.Label.TrimEnd('…')} completed for {drop.Paths.Count} selected item(s).");
            }
            else
            {
                var submitted = await submissions.SubmitAsync(drop.Operation, drop.Paths, ConfirmAsync, options, destination, lifetime.Token);
                jobs.AddRange(submitted);
                home.AddJobs(submitted);
                home.Report(submitted.Count == 0 ? "Canceled. No work was submitted." : $"Submitted {submitted.Count} job(s). Each ordinary input has its own result.");
            }
        }
        catch (OperationCanceledException) { home.Report("Canceled. No new work was submitted."); }
        catch (Exception error) { home.Report("Could not complete: " + error.Message); }
        finally { preparing = false; home.SetBusy(false); completed.TrySetResult(); }
    }
    internal async Task RevealAsync(IReadOnlyList<string> paths)
    {
        try { if (!IsQuitting) await actions!.ExecuteAsync("action.reveal", paths); }
        catch (Exception error) { home?.Report("Could not reveal output: " + error.Message); }
    }
    private Task<SubmissionChoice?> ConfirmAsync(SubmissionDraft draft, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new ToolOptionsWindow(draft) { Owner = home };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Choice : null);
    }
    private void JobChanged(ConversionJob job)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            if (IsQuitting) return;
            home?.RefreshJobs();
            if (job.State is ConversionJobState.Succeeded or ConversionJobState.Failed && notified.Add(job))
            {
                feedbackAudio?.Play(job.State == ConversionJobState.Succeeded ? FeedbackCue.Success : FeedbackCue.Error);
                tray?.ShowBalloonTip(3000, "Citrus", job.State == ConversionJobState.Succeeded ? "Your copies are ready. Open Citrus to reveal them." : "An item failed. Open Citrus for details and retry options.",
                    job.State == ConversionJobState.Succeeded ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
                if (settings.RevealCompletedOutputs && job.State == ConversionJobState.Succeeded && job.Outputs.Count > 0)
                    await RevealAsync(job.Outputs);
            }
        }));
    }
    internal async Task QuitAsync()
    {
        if (IsQuitting) return;
        IsQuitting = true;
        lifetime.Cancel();
        monitor?.Dispose();
        wheel?.Dismiss();
        settingsWindow?.Close();
        foreach (Window dialog in Windows.Cast<Window>().Where(window => window is ToolOptionsWindow).ToArray()) dialog.Close();
        if (tray is not null) tray.Visible = false;
        foreach (var job in jobs) job.Cancel();
        await activeAction; // A native Move is never abandoned midway through its filesystem work.
        await Task.WhenAll(jobs.Select(job => job.Completion));
        if (discovery is not null)
        {
            try { await discovery; }
            catch (Exception) { /* Initialization has its own status path; shutdown still releases resident resources. */ }
        }
        if (instance is not null) { await instance.DisposeAsync(); instance = null; }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        lifetime.Cancel();
        monitor?.Dispose();
        if (queue is not null) queue.JobChanged -= JobChanged;
        shelf?.Dispose();
        themes?.Dispose();
        feedbackAudio?.Dispose();
        tray?.ContextMenuStrip?.Dispose();
        tray?.Dispose();
        trayIcon?.Dispose();
        instance?.Dispose(); instance = null;
        lifetime.Dispose();
        ResourcesDisposed = true;
        base.OnExit(e);
    }
}
