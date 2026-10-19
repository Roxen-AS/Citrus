using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Core;
using Application = System.Windows.Application;
using Grid = System.Windows.Controls.Grid;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WorkspaceRenderingTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(1180, 820, ThemeChoice.Light, 1d)]
    [InlineData(1180, 820, ThemeChoice.Light, 1.5d)]
    [InlineData(1180, 820, ThemeChoice.Light, 2d)]
    [InlineData(1180, 820, ThemeChoice.Dark, 1d)]
    [InlineData(1180, 820, ThemeChoice.Dark, 1.5d)]
    [InlineData(1180, 820, ThemeChoice.Dark, 2d)]
    [InlineData(860, 640, ThemeChoice.Light, 1d)]
    [InlineData(860, 640, ThemeChoice.Light, 1.5d)]
    [InlineData(860, 640, ThemeChoice.Light, 2d)]
    [InlineData(860, 640, ThemeChoice.Dark, 1d)]
    [InlineData(860, 640, ThemeChoice.Dark, 1.5d)]
    [InlineData(860, 640, ThemeChoice.Dark, 2d)]
    public Task Workspace_renders_and_inspector_remains_reachable(int width, int height, ThemeChoice theme, double scale) => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string path = Path.Combine(files.Root, "sample.png");
        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgr32, null, Enumerable.Repeat((byte)160, 64 * 64 * 4).ToArray(), 64 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        using var themes = new ThemeManager(Application.Current);
        themes.Apply(theme, reduceMotion: true);
        using var window = new MainWindow((App)Application.Current) { Width = width, Height = height, ShowActivated = false };
        try
        {
            window.Configure(new OperationCatalog([new ImageEngine()]), []);
            window.AddFiles([path]);
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var inspector = (FrameworkElement)window.FindName("Inspector");
            var root = (FrameworkElement)window.Content;
            var requested = new { requestedWindowWidthDip = width, requestedWindowHeightDip = height };
            string artifact = $"workspace-requested-{width}x{height}-{theme}-{scale * 100:0}pct-raster";
            VisualArtifacts.Capture(root, artifact, scale, theme, requested);
            // HWND size may be clamped to the hosted desktop work area. Production adapts to ActualWidth.
            Assert.Equal(window.ActualWidth < 1080 ? 1 : 2, Grid.GetColumn(inspector));
            Assert.True(((System.Windows.Controls.Button)window.FindName("RunButton")).IsEnabled);
            var run = (FrameworkElement)window.FindName("RunButton"); run.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var position = run.TransformToAncestor(root).Transform(new System.Windows.Point(0, 0));
            VisualArtifacts.Capture(root, artifact + "-inspector", scale, theme, requested);
            Assert.InRange(position.Y, 0, root.ActualHeight - run.ActualHeight + 1);
            VisualArtifacts.AssertInside(run, root);
        }
        finally { window.Dispose(); window.Close(); themes.Apply(ThemeChoice.Light, reduceMotion: true); }
    });

    [Fact]
    public Task Late_preview_cannot_replace_the_current_file() => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string first = files.File("first.png", "sample"), second = files.File("second.png", "sample");
        var previews = new DelayedPreview();
        using var window = new MainWindow((App)Application.Current, previews);
        try
        {
            window.AddFiles([first, second]);
            ((ListBox)window.FindName("SelectedFiles")).SelectedIndex = 1;
            ImageSource current = new DrawingImage(); current.Freeze();
            ImageSource stale = new DrawingImage(); stale.Freeze();
            previews.Requests[1].SetResult(current);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            previews.Requests[0].SetResult(stale);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(current, ((Image)window.FindName("PreviewImage")).Source);
        }
        finally { window.Dispose(); window.Close(); }
    });

    private sealed class DelayedPreview : IFilePreviewService
    {
        public List<TaskCompletionSource<ImageSource?>> Requests { get; } = [];
        public Task<ImageSource?> LoadAsync(string path, int pixelLimit, CancellationToken token)
        {
            var request = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(request); return request.Task; // Intentionally ignores cancellation to prove the generation guard.
        }
    }
}
