using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using Citrus.Core;
using Microsoft.Win32;

namespace Citrus.Windows;

public partial class MainWindow : Window
{
    private readonly App app;
    private OperationCatalog? catalog;
    private readonly ObservableCollection<OutputViewModel> inputs = [];
    private readonly ObservableCollection<JobViewModel> jobs = [];
    private bool busy;
    public MainWindow(App app)
    {
        this.app = app;
        InitializeComponent();
        SelectedFiles.ItemsSource = inputs;
        JobList.ItemsSource = jobs;
        Closing += OnClosing;
    }
    public void Configure(OperationCatalog available, IReadOnlyList<string> warnings)
    {
        catalog = available;
        EngineStatus.Text = warnings.Count == 0 ? "Image, media, PDF, subtitle and archive engines ready. Formats depend on the selection." : string.Join("\n", warnings);
        RefreshOperations();
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!app.IsQuitting) { e.Cancel = true; Hide(); } }
    private void ChooseFiles(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose local files", CheckFileExists = true, Multiselect = true, Filter = "All files|*.*" };
        if (picker.ShowDialog(this) == true) AddFiles(picker.FileNames);
    }
    private void AddFiles(IReadOnlyList<string> paths)
    {
        if (!DropValidation.Accepts(paths, OperationMode.Convert)) { Report("Choose physical local files. Folders and unavailable cloud files cannot be converted."); return; }
        foreach (string path in paths)
            if (!inputs.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) inputs.Add(new(path));
        RefreshOperations();
    }
    private void FilesDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (e.AllowedEffects.HasFlag(DragDropEffects.Copy) && e.Data.GetDataPresent(DataFormats.FileDrop, false) && e.Data.GetData(DataFormats.FileDrop, false) is string[] paths && DropValidation.Accepts(paths, OperationMode.Convert)) e.Effects = DragDropEffects.Copy;
    }
    private void FilesDropped(object sender, DragEventArgs e)
    {
        FilesDragOver(sender, e);
        if (e.Effects == DragDropEffects.Copy && e.Data.GetData(DataFormats.FileDrop, false) is string[] paths) AddFiles(paths);
    }
    private void FileSelectionChanged(object sender, SelectionChangedEventArgs e) { if (RemoveButton is not null) RemoveButton.IsEnabled = SelectedFiles.SelectedItems.Count > 0; }
    private void RemoveFiles(object sender, RoutedEventArgs e) { foreach (var item in SelectedFiles.SelectedItems.Cast<OutputViewModel>().ToArray()) inputs.Remove(item); RefreshOperations(); }
    private void ClearFiles(object sender, RoutedEventArgs e) { inputs.Clear(); RefreshOperations(); }
    private void ModeChanged(object sender, SelectionChangedEventArgs e) { if (Operations is not null) RefreshOperations(); }
    private void OperationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RunButton is null) return;
        var operation = Operations.SelectedItem as OperationDescriptor;
        RunButton.IsEnabled = !busy && operation is not null;
        RunButton.Content = operation?.IsGroup == true ? "Create group…" : operation?.NeedsOptions == true ? "Set options…" : "Create copies";
        OperationHint.Text = operation?.Id.StartsWith("image.", StringComparison.Ordinal) == true || operation?.Id == "images.pdf"
            ? "Still-image route: single-frame images and SVG input. Animated and multipage images are rejected."
            : operation?.Id == "media.convert.gif" ? "Creates GIF animation through the media engine."
            : operation?.Id == "archive.extract" ? "Extraction stops at 100,000 entries or 20 GiB. GZIP yields one file; nested archives need a second extraction."
            : "";
    }
    private void RefreshOperations()
    {
        string? previous = (Operations.SelectedItem as OperationDescriptor)?.Id;
        var available = catalog?.GetOperations(inputs.Select(i => i.Path).ToArray(), Modes.SelectedIndex == 1 ? OperationMode.Tools : OperationMode.Convert) ?? [];
        Operations.ItemsSource = available;
        Operations.SelectedItem = available.FirstOrDefault(o => o.Id == previous) ?? available.FirstOrDefault();
        ClearButton.IsEnabled = inputs.Count > 0;
        SelectionStatus.Text = $"{inputs.Count} input(s) · drop order retained";
        if (!busy) Report(catalog is null ? "Discovering installed engines…" : inputs.Count == 0 ? "Add files to see compatible operations." : available.Count == 0 ? "No compatible operations for this entire selection. Remove an input or switch mode." : "Only operations supported by every selected input are shown. Originals stay in place.");
        RunButton.IsEnabled = !busy && Operations.SelectedItem is OperationDescriptor;
    }
    private async void RunOperation(object sender, RoutedEventArgs e)
    {
        if (Operations.SelectedItem is OperationDescriptor operation) await app.SubmitAsync(new(operation, inputs.Select(i => i.Path).ToArray()));
    }
    public void SetBusy(bool value) { busy = value; RunButton.IsEnabled = !busy && Operations.SelectedItem is OperationDescriptor; }
    public void AddJobs(IReadOnlyList<ConversionJob> submitted) { foreach (var job in submitted) jobs.Insert(0, new(job)); RefreshJobs(); }
    public void RefreshJobs()
    {
        foreach (var job in jobs) job.Refresh();
        JobSummary.Text = $"Jobs · {jobs.Count(j => j.Job.State == ConversionJobState.Succeeded)} succeeded · {jobs.Count(j => j.Job.State == ConversionJobState.Failed)} failed · {jobs.Count(j => j.Job.State == ConversionJobState.Canceled)} canceled · {jobs.Count(j => j.CanCancel)} pending";
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
    private void RevealOutput(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string path) return;
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) { Report("That output no longer exists at its saved location."); return; }
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path);
            Process.Start(start)?.Dispose();
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException) { Report("Could not reveal the output: " + error.Message); }
    }
    private void ToggleGestures(object sender, RoutedEventArgs e) => app.SetGestures(GesturesEnabled.IsChecked == true);
    public void SetGestureCheck(bool enabled) => GesturesEnabled.IsChecked = enabled;
    public void Report(string message) => Status.Text = message;
}
