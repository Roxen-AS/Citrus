using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Citrus.Conversion;
using Citrus.Conversion.Editing.Image;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Image;
using Microsoft.Win32;

namespace Citrus.Windows.Editors;

public partial class EditorHostWindow : Window
{
    public static IReadOnlyList<string> Tools { get; } = Array.AsReadOnly(new[] { "Adjust", "Crop", "Resize", "Rotate", "Compress" });
    private readonly ImageEditorServices services;
    private readonly ImageRecipeRenderer renderer;
    private readonly EditorSession session;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? previewWork;
    private EditorHostViewModel? model;
    private ImageEditorView? inspector;
    private EditorSubmissionBatch? accepted;
    private long generation;
    private bool closed, loading, shown, changingSelection;
    private string previousTool = "Adjust";
    internal EditorHostViewModel? Model => model;
    public EditorHostWindow(ImageEditorServices services)
    {
        this.services = services; renderer = services.CreateSessionRenderer(); session = new(renderer);
        InitializeComponent(); DataContext = this; ToolPicker.ItemsSource = Tools;
        CropHandles.Changed += CropChanged;
        PreviewKeyDown += KeyPressed; Closing += CheckClose;
        Closed += (_, _) => { closed = true; session.Close(); lifetime.Cancel(); previewWork?.Cancel(); };
        // Attach after all lifetime/placement wiring; ordinary windows use the shared controller and button springs.
        (Application.Current as App)?.AttachPresentation(this, Chrome);
    }
    public async Task<EditorSubmissionBatch?> ShowEditorAsync(EditorOpenRequest request, CancellationToken ct)
    {
        if (shown) throw new InvalidOperationException("An editor window can be shown once."); shown = true;
        model = new(request, services); SourceList.ItemsSource = model.Sources;
        Heading.Text = request.Operation.Label; Title = request.Operation.Label + " · Citrus";
        ToolPicker.SelectedItem = Tools.Contains(request.EntryTool) ? request.EntryTool : "Adjust";
        Loaded += LoadInitial;
        using var registration = ct.Register(() => Dispatcher.BeginInvoke(new Action(() => { lifetime.Cancel(); if (!closed) Close(); })));
        try { ct.ThrowIfCancellationRequested(); ShowDialog(); return accepted; }
        finally
        {
            session.Close(); lifetime.Cancel(); previewWork?.Cancel();
            await renderer.DisposeAsync(); previewWork?.Dispose(); lifetime.Dispose();
        }
        async void LoadInitial(object sender, RoutedEventArgs e)
        {
            Loaded -= LoadInitial;
            await Capture(request.InitialPaths, false);
        }
    }
    private async Task Capture(ImmutableArray<string> paths, bool imported)
    {
        loading = true; ExportButton.IsEnabled = false; PreviewStatus.Text = "Capturing and inspecting original images…";
        try
        {
            await model!.CaptureAsync(paths, imported, lifetime.Token);
            foreach (var row in model.Sources) { row.PropertyChanged -= RowChanged; row.PropertyChanged += RowChanged; }
            SourceList.SelectedItem = model.Selected ?? model.Sources.FirstOrDefault();
            RebuildInspector(); RefreshPreview(); UpdateSummary();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException) { if (!closed) ErrorText.Text = error.Message; }
        finally { loading = false; if (!closed) { ExportButton.IsEnabled = true; UpdateSummary(); } }
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e) => UpdateSummary();
    private void UpdateSummary()
    {
        if (model is null) return;
        IncludedText.Text = $"{model.Sources.Count(r => r.Included)} of {model.Sources.Count} included";
        DestinationText.Text = model.Destination ?? "Save new copies beside each original";
    }
    private void SelectSource(object sender, SelectionChangedEventArgs e)
    {
        if (model is null || changingSelection) return;
        try { inspector?.FlushFields(); }
        catch (ArgumentException error)
        {
            ErrorText.Text = error.Message; changingSelection = true; SourceList.SelectedItem = model.Selected; changingSelection = false; return;
        }
        model.CommitGesture(); model.Selected = SourceList.SelectedItem as EditorSourceRow; CropHandles.AspectRatio = null;
        PreviewImage.Source = null; CropHandles.CancelDrag(); RebuildInspector(); RefreshPreview();
    }
    private void InclusionChanged(object sender, RoutedEventArgs e) => UpdateSummary();
    private string Tool => ToolPicker.SelectedItem as string ?? "Adjust";
    private void ToolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (model is null || changingSelection) return;
        try { inspector?.FlushFields(); }
        catch (ArgumentException error)
        {
            ErrorText.Text = error.Message; changingSelection = true; ToolPicker.SelectedItem = previousTool; changingSelection = false; return;
        }
        previousTool = Tool; model.CommitGesture(); CropHandles.CancelDrag(); CropHandles.AspectRatio = null; RebuildInspector(); RefreshPreview();
    }
    private void RebuildInspector()
    {
        Inspector.Children.Clear(); inspector = null;
        CropHandles.Visibility = Tool == "Crop" && model?.Selected?.Draft is not null ? Visibility.Visible : Visibility.Collapsed;
        DetailPreview.IsEnabled = Tool != "Crop";
        if (model?.Selected?.Draft is null) { Inspector.Children.Add(new TextBlock { Text = model?.Selected?.Error ?? "Select an image to edit.", TextWrapping = TextWrapping.Wrap }); return; }
        inspector = new(model, Tool, RefreshPreview, message => ErrorText.Text = message, value => CropHandles.AspectRatio = value, RebuildInspector, CropHandles.AspectRatio);
        Inspector.Children.Add(inspector);

    }
    private void CropChanged(RectD rect, bool commit)
    {
        try
        {
            if (model?.Selected?.Draft is not { Recipe: ImageRecipe recipe } doc) return;
            model.Change(doc with { Recipe = recipe with { Geometry = recipe.Geometry with { Crop = rect } } }, transient: true);
            if (commit) { model.CommitGesture(); RebuildInspector(); }
            RefreshPreview(); ErrorText.Text = "";
        }
        catch (ArgumentException error) { ErrorText.Text = error.Message; }
    }
    private async void RefreshPreview()
    {
        if (closed) return;
        previewWork?.Cancel(); long current = ++generation;
        if (model?.Selected is not { Draft: { Recipe: ImageRecipe recipe } document } row)
        {
            PreviewImage.Source = null; PreviewStatus.Text = model?.Selected?.Error ?? "Select an image to preview."; return;
        }
        var work = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); previewWork = work;
        var previewDocument = document;
        if (Tool == "Crop")
        {
            var size = recipe.Geometry.QuarterTurns % 2 == 0 ? row.Size : new SizeI(row.Size.Height,row.Size.Width);
            CropHandles.Plane = size; CropHandles.Crop = recipe.Geometry.Crop ?? new(0,0,size.Width,size.Height); CropHandles.InvalidateVisual();
            // Crop context is the whole G plane; the overlay records the real crop in the draft.
            previewDocument = document with { Recipe = recipe with { Geometry = recipe.Geometry with { Crop = null, OutputSize = null } } };
        }
        RectD? tile = null;
        if (DetailPreview.IsChecked == true && Tool != "Crop")
        {
            var (_, output) = ImageGeometryMapper.Resolve(recipe, row.Size);
            var center = ImageGeometryMapper.MapFromCanvas(recipe, row.Size, new(output.Width/2d,output.Height/2d));
            double width = Math.Min(512, row.Size.Width), height = Math.Min(512,row.Size.Height);
            tile = new(Math.Clamp(center.X-width/2,0,row.Size.Width-width),Math.Clamp(center.Y-height/2,0,row.Size.Height-height),width,height);
        }
        session.Update(previewDocument); PreviewStatus.Text = "Rendering preview…";
        try
        {
            await Task.Delay(120, work.Token);
            var frame = await session.PreviewAsync(new(current, new(512,512), null,null,tile), work.Token);
            if (frame is null) return;
            var bitmap = await Task.Run(() =>
            {
                var result = BitmapSource.Create(frame.Size.Width,frame.Size.Height,96,96,PixelFormats.Bgra32,null,frame.Bgra32.ToArray(),frame.Stride);
                result.Freeze(); return result;
            }, work.Token);
            if (closed || current != generation || work.IsCancellationRequested) return;
            PreviewImage.Stretch = tile is not null ? Stretch.None : Stretch.Uniform;
            PreviewImage.Source = bitmap;
            PreviewStatus.Text = $"{frame.Size.Width} × {frame.Size.Height} preview pixels · " +
                (Tool == "Crop" ? "Full oriented image with crop overlay. " : tile is not null ? "Exact source detail. " : "") + string.Join(" ",frame.Warnings);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!closed && current == generation) { PreviewImage.Source = null; PreviewStatus.Text = "Preview unavailable. " + error.Message; } }
        finally { if (ReferenceEquals(previewWork, work)) previewWork = null; work.Dispose(); }
    }
    private void DetailChanged(object sender, RoutedEventArgs e) => RefreshPreview();
    private void Undo(object sender, RoutedEventArgs e) { model?.Undo(); RebuildInspector(); RefreshPreview(); }
    private void Redo(object sender, RoutedEventArgs e) { model?.Redo(); RebuildInspector(); RefreshPreview(); }
    private void Reset(object sender, RoutedEventArgs e) { model?.Reset(); RebuildInspector(); RefreshPreview(); }
    private void ApplyAll(object sender, RoutedEventArgs e)
    {
        try { inspector?.FlushFields(); model!.ApplyToAll(MappingMode.SelectedIndex == 1 ? ImageBatchGeometry.Pixels : ImageBatchGeometry.Relative, model.Selected?.ResizeIntent); ErrorText.Text = ""; }
        catch (ArgumentException error) { ErrorText.Text = error.Message; }
    }
    private async void AddImages(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        var picker = new OpenFileDialog { Title = "Add independent images", Multiselect = true, Filter = "Raster images|*.png;*.jpg;*.jpeg;*.webp;*.tif;*.tiff;*.bmp;*.gif;*.avif|All files|*.*" };
        if (picker.ShowDialog(this) == true) await Capture([.. picker.FileNames], true);
    }
    private async void ReloadImage(object sender, RoutedEventArgs e)
    {
        if (loading || model?.Selected is not { } row) return;
        if (row.Dirty && MessageBox.Show(this,"Reload this original and discard its draft edits?","Reload image",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No) != MessageBoxResult.Yes) return;
        loading = true; ExportButton.IsEnabled = false; previewWork?.Cancel(); PreviewImage.Source = null;
        try { await model.ReloadAsync(row,lifetime.Token); RebuildInspector(); RefreshPreview(); UpdateSummary(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException) { if (!closed) ErrorText.Text = error.Message; }
        finally { loading = false; if (!closed) ExportButton.IsEnabled = true; }
    }
    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Save image copies to" };
        if (picker.ShowDialog(this) == true) { model!.Destination = picker.FolderName; UpdateSummary(); }
    }
    private void Export(object sender, RoutedEventArgs e)
    {
        if (loading || accepted is not null) return;
        try { inspector?.FlushFields(); accepted = model!.Accept(); DialogResult = true; }
        catch (ArgumentException error) { ErrorText.Text = error.Message; }
    }
    private void Cancel(object sender, RoutedEventArgs e) => Close();
    private void CheckClose(object? sender, CancelEventArgs e)
    {
        if (accepted is not null || lifetime.IsCancellationRequested || model?.Dirty != true) return;
        e.Cancel = MessageBox.Show(this,"Discard these edits? Your original files remain unchanged.","Keep editing?",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No) != MessageBoxResult.Yes;
    }
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (CropHandles.IsDragging || inspector?.GestureActive == true) { CropHandles.CancelDrag(); model?.CancelGesture(); RebuildInspector(); RefreshPreview(); }
            else Close();
            e.Handled = true;
        }
        // Text editing keeps its native undo/redo and Enter never triggers export.
        if (Keyboard.FocusedElement is TextBoxBase) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
        { if (e.Key == Key.Z) Undo(this,e); else Redo(this,e); e.Handled = true; }
    }
}
