using System.ComponentModel;
using Citrus.Core;
using Microsoft.Win32;

namespace Citrus.Windows;

public partial class MainWindow : Window
{
    private readonly App app;
    public MainWindow(App app, IReadOnlyList<OperationDescriptor> operations)
    {
        this.app = app;
        InitializeComponent();
        Formats.ItemsSource = operations.Where(operation => operation.Mode == OperationMode.Convert).ToArray();
        Formats.SelectedIndex = 0;
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!app.IsQuitting) { e.Cancel = true; Hide(); }
    }
    private void ChooseImage(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose an image", CheckFileExists = true, Multiselect = false, Filter = "Images and SVG|*.png;*.jpg;*.jpeg;*.webp;*.tif;*.tiff;*.bmp;*.gif;*.avif;*.svg" };
        if (picker.ShowDialog(this) == true && Formats.SelectedItem is OperationDescriptor operation)
            app.Submit(new(operation, [picker.FileName]));
    }
    private void ToggleGestures(object sender, RoutedEventArgs e) => app.SetGestures(GesturesEnabled.IsChecked == true);
    public void SetGestureCheck(bool enabled) => GesturesEnabled.IsChecked = enabled;
    public void Report(string message) => Status.Text = message;
}
