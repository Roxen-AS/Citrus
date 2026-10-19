using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Interop;
using Citrus.Core;
using Microsoft.Win32;

namespace Citrus.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly App app;
    private OperationCatalog? catalog;
    private readonly WorkspaceSelection selection = new();
    private readonly IFilePreviewService previews;
    private CancellationTokenSource? previewCancellation;
    private long previewGeneration;
    private bool disposed;
    private WheelDemoWindow? demo;
    private readonly ObservableCollection<JobViewModel> jobs = [];
    private readonly ObservableCollection<SubmissionFailure> failures = [];
    private bool busy;
    public MainWindow(App app, IFilePreviewService? previews = null)
    {
        this.app = app;
        this.previews = previews ?? new FilePreviewService();
        InitializeComponent();
        // The HWND exists here but has not been shown: size/position before first paint.
        SourceInitialized += PlaceInitialWorkspace;
        SelectedFiles.ItemsSource = selection.Items;
        selection.MembershipChanged += MembershipChanged;
        JobList.ItemsSource = jobs;
        SubmissionFailures.ItemsSource = failures;
        Closing += OnClosing;
        IsVisibleChanged += async (_, _) =>
        {
            if (disposed) return;
            if (IsVisible) await RefreshPreviewAsync();
            else StopPreview();
        };
        (Application.Current as App)?.AttachPresentation(this, Chrome);
    }
    private void PlaceInitialWorkspace(object? sender, EventArgs e)
    {
        SourceInitialized -= PlaceInitialWorkspace; // Initial placement only; later user moves/resizes stay untouched.
        if (!NativeMethods.GetCursorPos(out var pointer)) return;
        var work = NativeMethods.WorkArea(pointer);
        double scale = NativeMethods.MonitorScale(pointer);
        double availableWidth = work.Width / scale, availableHeight = work.Height / scale;
        double requestedWidth = Width, requestedHeight = Height;
        // On displays smaller than the minimum, visible/reachable chrome takes precedence.
        // Lower only that dimension's minimum; never install monitor-sized maximum bounds.
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        int width = (int)Math.Floor(Math.Min(work.Width, Math.Max(MinWidth, requestedWidth) * scale));
        int height = (int)Math.Floor(Math.Min(work.Height, Math.Max(MinHeight, requestedHeight) * scale));
        int left = (int)Math.Floor(work.Left + (work.Width - width) / 2);
        int top = (int)Math.Floor(work.Top + (work.Height - height) / 2);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width / scale; Height = height / scale;
        Left = left / scale; Top = top / scale;
        // Work-area origins are physical coordinates, including negative mixed-monitor origins.
        // Native placement avoids assuming the target monitor shares the primary monitor's DPI.
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, 0, left, top, width, height, 0x14); // NOACTIVATE | NOZORDER
    }

    public void Configure(OperationCatalog available, IReadOnlyList<string> warnings)
    {
        catalog = available;
        EngineStatus.Text = warnings.Count == 0 ? "Image, media, PDF, subtitle and archive engines ready. Formats depend on the selection." : string.Join("\n", warnings);
        RefreshOperations();
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!app.IsQuitting && !disposed) { e.Cancel = true; demo?.Close(); Hide(); } }
    private void ChooseFiles(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose local files", CheckFileExists = true, Multiselect = true, Filter = "All files|*.*" };
        if (picker.ShowDialog(this) == true) AddFiles(picker.FileNames);
    }
    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose physical folders for Actions", Multiselect = true };
        if (picker.ShowDialog(this) == true) AddFiles(picker.FolderNames);
    }
    internal void AddFiles(IReadOnlyList<string> paths)
    {
        if (!DropValidation.Accepts(paths, OperationMode.Actions)) { Report("Choose existing physical files or folders. Unavailable cloud placeholders cannot be used."); return; }
        selection.Add(paths);
        SelectedFiles.SelectedItem = selection.Active;
        if (paths.Any(Directory.Exists)) Modes.SelectedIndex = 2;
        RefreshOperations();
    }
    private void FilesDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (e.AllowedEffects.HasFlag(DragDropEffects.Copy) && e.Data.GetDataPresent(DataFormats.FileDrop, false) && e.Data.GetData(DataFormats.FileDrop, false) is string[] paths && DropValidation.Accepts(paths, OperationMode.Actions)) e.Effects = DragDropEffects.Copy;
    }
    private void FilesDropped(object sender, DragEventArgs e)
    {
        FilesDragOver(sender, e);
        if (e.Effects == DragDropEffects.Copy && e.Data.GetData(DataFormats.FileDrop, false) is string[] paths) AddFiles(paths);
    }
    private async void FileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RemoveButton is null || disposed) return;
        selection.Preview(SelectedFiles.SelectedItem as WorkspaceInput);
        RemoveButton.IsEnabled = selection.Active is not null;
        await RefreshPreviewAsync();
    }
    private void MembershipChanged()
    {
        if (disposed) return;
        RefreshOperations();
        UpdatePreviewCaption();
    }
    private void UpdatePreviewCaption() => PreviewCaption.Text = selection.Active is { } active
        ? active.Name + (active.IsIncluded ? " · included" : " · excluded from batch") : "Add a file to preview it here";
    private async Task RefreshPreviewAsync()
    {
        previewCancellation?.Cancel(); previewCancellation?.Dispose();
        previewCancellation = new();
        var token = previewCancellation.Token;
        long generation = ++previewGeneration;
        PreviewImage.Source = null;
        PreviewFallback.Visibility = Visibility.Visible;
        UpdatePreviewCaption();
        if (selection.Active is not { } active) { PreviewMessage.Text = "A little room for your next idea"; return; }
        PreviewMessage.Text = "Loading preview…";
        try
        {
            // Preview-only navigation does not rebuild the operation catalog or change batch inclusion.
            var image = await previews.LoadAsync(active.Path, 640, token);
            if (disposed || token.IsCancellationRequested || generation != previewGeneration) return;
            PreviewImage.Source = image;
            PreviewFallback.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
            PreviewMessage.Text = "Preview unavailable\n" + (Path.GetExtension(active.Path) is { Length: > 0 } extension ? extension.TrimStart('.').ToUpperInvariant() + " file" : "File or folder") + " · use a compatible operation below";
            PreviewCaption.ToolTip = active.Path;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            if (!disposed && generation == previewGeneration) PreviewMessage.Text = "Preview unavailable. Your file can still be included.";
        }
    }
    private void RemoveFiles(object sender, RoutedEventArgs e)
    {
        if (selection.Active is { } active) selection.Remove(active);
        SelectedFiles.SelectedItem = selection.Active;
    }
    private void ClearFiles(object sender, RoutedEventArgs e) { selection.Clear(); SelectedFiles.SelectedItem = null; }
    private void IncludeAll(object sender, RoutedEventArgs e) => selection.SetAllIncluded(true);
    private void ExcludeAll(object sender, RoutedEventArgs e) => selection.SetAllIncluded(false);
    private void ModeChanged(object sender, SelectionChangedEventArgs e) { if (Operations is not null) RefreshOperations(); }
    private void OperationChanged(object sender, SelectionChangedEventArgs e) => UpdateInspector();
    private void UpdateInspector()
    {
        if (RunButton is null) return;
        var operation = (Operations.SelectedItem as OperationCard)?.Operation;
        RunButton.IsEnabled = !busy && operation is not null;
        RunButton.Content = operation?.Mode == OperationMode.Actions ? operation.Label : operation?.IsGroup == true ? "Review group…" : operation?.NeedsOptions == true ? "Set options…" : $"Create {selection.IncludedPaths.Count} copies";
        InspectorTitle.Text = operation?.Label ?? "Choose files and an operation";
        OperationHint.Text = operation is null ? "Only compatible operations for every included file appear here." : OperationCard.Describe(operation);
        BatchSummary.Text = $"{selection.IncludedPaths.Count} included file(s)";
        OutputSummary.Text = operation?.Mode == OperationMode.Actions ? "Applies to the included physical files and folders."
            : operation?.IsGroup == true ? "One grouped output · review order and destination next"
            : $"{operation?.OutputExtension?.TrimStart('.').ToUpperInvariant() ?? "New"} copies · beside each original";
        SafetySummary.Text = operation?.Id == "action.move" ? "Moves originals to a folder you choose. Existing items are never replaced."
            : operation?.Mode == OperationMode.Actions ? "Review the action above before continuing." : "Originals stay untouched. Existing files are never replaced.";
    }
    private void RefreshOperations()
    {
        string? previous = (Operations.SelectedItem as OperationCard)?.Operation.Id;
        var paths = selection.IncludedPaths;
        var available = catalog?.GetOperations(paths, Modes.SelectedIndex == 2 ? OperationMode.Actions : Modes.SelectedIndex == 1 ? OperationMode.Tools : OperationMode.Convert) ?? [];
        var cards = available.Select(operation => new OperationCard(operation)).ToArray();
        Operations.ItemsSource = cards;
        Operations.SelectedItem = cards.FirstOrDefault(card => card.Operation.Id == previous) ?? cards.FirstOrDefault();
        ClearButton.IsEnabled = selection.Items.Count > 0;
        SelectionStatus.Text = $"{paths.Count} of {selection.Items.Count} included";
        OperationsEmpty.Text = catalog is null ? "Discovering installed engines…" : selection.Items.Count == 0 ? "Add files to discover what you can create."
            : paths.Count == 0 ? "Check at least one file to see compatible operations."
            : available.Count == 0 ? "No compatible operations for all included files. Exclude an input or switch mode." : "";
        UpdateInspector();
    }

    private async void RunOperation(object sender, RoutedEventArgs e)
    {
        if (Operations.SelectedItem is OperationCard card) await app.SubmitAsync(new(card.Operation, selection.IncludedPaths), origin: SubmissionOrigin.Home);
    }
    public void SetBusy(bool value) { busy = value; RunButton.IsEnabled = !busy && Operations.SelectedItem is OperationCard; }
    public void AddJobs(IReadOnlyList<ConversionJob> submitted) { foreach (var job in submitted) jobs.Insert(0, new(job)); RefreshJobs(); if (IsVisible && submitted.Count > 0) JobsPanel.IsExpanded = true; }
    public void RefreshJobs()
    {
        foreach (var job in jobs) job.Refresh();
        JobSummary.Text = $"Jobs · {jobs.Count(j => j.Job.State == ConversionJobState.Succeeded)} succeeded · {jobs.Count(j => j.Job.State == ConversionJobState.Failed) + failures.Count} failed · {jobs.Count(j => j.Job.State == ConversionJobState.Canceled)} canceled · {jobs.Count(j => j.CanCancel)} pending";
    }
    private void CancelJob(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is JobViewModel model) model.Job.Cancel(); }
    private async void RetryJob(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is JobViewModel model && model.CanRetry) await app.RetryAsync(model.Job, model.Job.Request.DestinationDirectory);
    }
    private async void ChangeDestination(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not JobViewModel model || !model.CanChooseFolder) return;
        var picker = new OpenFolderDialog { Title = "Choose another folder and retry" };
        if (picker.ShowDialog(this) == true) await app.RetryAsync(model.Job, picker.FolderName);
    }
    private async void RevealOutput(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is string path) await app.RevealAsync([path]);
    }
    private void OpenSettings(object sender, RoutedEventArgs e) => app.OpenSettings();
    private void ToggleGestures(object sender, RoutedEventArgs e) { if (IsLoaded) app.SetGestures(GesturesEnabled.IsChecked == true); }
    public void RefreshSelection()
    {
        foreach (var input in selection.Items.ToArray()) if (!File.Exists(input.Path) && !Directory.Exists(input.Path)) selection.Remove(input);
        SelectedFiles.SelectedItem = selection.Active;
        RefreshOperations();
    }
    public void UpdateGestureGuide(CitrusSettings settings) => GestureGuide.Text = $"Start dragging in Explorer, then tap a key. Tap Shift for {settings.ShiftMode}, Alt for {settings.AltMode}, or Control for {settings.ControlMode}.";
    public void SetGestureCheck(bool enabled) => GesturesEnabled.IsChecked = enabled;
    public void AddSubmissionFailure(string message, Func<Task>? retry)
    {
        failures.Insert(0, new(message, retry));
        if (IsVisible) JobsPanel.IsExpanded = true;
        RefreshJobs();
    }
    private async void RetrySubmission(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SubmissionFailure { Retry: { } retry })
        {
            var button = (Button)sender;
            button.IsEnabled = false;
            try { await retry(); }
            finally { button.IsEnabled = true; }
            // Preserve the failed attempt, just as failed queue jobs remain after a retry.
            RefreshJobs();
        }
    }
    public void Report(string message) => Status.Text = message;
    internal void ShowJobs() { JobsPanel.IsExpanded = true; JobsPanel.BringIntoView(); }
    private void WorkspaceResized(object sender, SizeChangedEventArgs e)
    {
        if (InspectorColumn is null) return;
        ApplyTextScale(TryFindResource("TextScaleFactor") is double scale ? scale : 1);
    }
    internal void ApplyTextScale(double scale)
    {
        if (InspectorColumn is null) return;
        bool large = scale > 1.25, compact = ActualWidth < 1080 || large;
        FileColumn.Width = new(large ? 0 : compact ? 220 : 240);
        InspectorColumn.Width = new(compact ? 0 : 304);
        var files = (FrameworkElement)WorkspaceGrid.Children[0];
        Grid.SetColumn(files, large ? 1 : 0); Grid.SetRow(files, 0); Grid.SetRowSpan(files, large ? 1 : 2);
        Grid.SetRow(CenterPanel, large ? 1 : 0);
        Grid.SetColumn(Inspector, compact ? 1 : 2); Grid.SetRow(Inspector, large ? 2 : compact ? 1 : 0);
        if (WorkspaceGrid.RowDefinitions.Count == 2) WorkspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Inspector.Margin = compact ? new(0, 16, 0, 0) : new(0);
        CenterPanel.Margin = large ? new(0, 16, 0, 0) : compact ? new(0) : new(0, 0, 16, 0);
        Grid.SetRow(HeaderActions, large ? 1 : 0); Grid.SetColumn(HeaderActions, large ? 0 : 1);
        Grid.SetColumnSpan(HeaderActions, large ? 2 : 1);
        HeaderActions.Margin = large ? new(0, 12, 0, 8) : new(0);
        WorkspaceScroll.VerticalScrollBarVisibility = large ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private void ShowDemo(object sender, RoutedEventArgs e)
    {
        if (demo is not null) { demo.Activate(); return; }
        demo = new() { Owner = this };
        app.AttachPresentation(demo, demo.Content as Citrus.Windows.Presentation.MaterialSurface);
        demo.Closed += (_, _) => demo = null;
        demo.Show();
    }
    private void StopPreview()
    {
        ++previewGeneration;
        previewCancellation?.Cancel(); previewCancellation?.Dispose(); previewCancellation = null;
        PreviewImage.Source = null;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopPreview();
        selection.MembershipChanged -= MembershipChanged;
        demo?.Close(); demo = null;
    }
}

public sealed record SubmissionFailure(string Message, Func<Task>? Retry) { public bool CanRetry => Retry is not null; }
