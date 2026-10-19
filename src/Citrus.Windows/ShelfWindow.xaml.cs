using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Citrus.Core;

namespace Citrus.Windows;

public partial class ShelfWindow : Window, IDisposable
{
    private readonly ObservableCollection<string> references = [];
    private readonly DispatcherTimer timer;
    private Point dragStart;
    private bool disposed, closed;
    public IReadOnlyList<string> Paths => references.ToArray();
    public ShelfWindow()
    {
        InitializeComponent();
        References.ItemsSource = references;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => RefreshReferences(), Dispatcher);
        Closed += (_, _) => { closed = true; Dispose(); };
    }
    public void AddPaths(IReadOnlyList<string> paths)
    {
        foreach (var path in FileActions.Validate(paths))
            if (!references.Contains(path, StringComparer.OrdinalIgnoreCase)) references.Add(path);
    }
    public void RefreshReferences()
    {
        foreach (var path in references.ToArray())
            if (!DropValidation.Accepts([path], OperationMode.Actions) || (!File.Exists(path) && !Directory.Exists(path))) references.Remove(path);
    }
    private string[] Selected() => References.SelectedItems.Cast<string>().ToArray();
    private void DragStarted(object sender, MouseButtonEventArgs e) => dragStart = e.GetPosition(this);
    private void DragMoved(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        RefreshReferences();
        var paths = Selected();
        if (paths.Length == 0) return;
        try { DragDrop.DoDragDrop(References, FileActions.CreateCopyData(paths), DragDropEffects.Copy); }
        catch (IOException error) { MessageBox.Show(this, error.Message, "Citrus shelf"); }
    }
    private async void CopySelected(object sender, RoutedEventArgs e)
    {
        RefreshReferences();
        if (Selected() is not { Length: > 0 } paths) return;
        try { await new FileActions().ExecuteAsync("action.copy", paths); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Citrus shelf"); }
    }
    private void RemoveSelected(object sender, RoutedEventArgs e) { foreach (string path in Selected()) references.Remove(path); }
    private void ShelfKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { RemoveSelected(sender, e); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { CopySelected(sender, e); e.Handled = true; }
    }
    public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); if (!closed) Close(); }
}
