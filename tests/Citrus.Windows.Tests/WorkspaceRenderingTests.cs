using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Core;
using Citrus.Windows.Presentation;
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
        CitrusPreviewFixture.Save(path);
        using var themes = new ThemeManager(Application.Current);
        themes.Apply(theme, reduceMotion: true);
        using var window = new MainWindow((App)Application.Current) { Width = width, Height = height, ShowActivated = false };
        using var presentation = new WindowPresentationService(Application.Current, themes, () => new CitrusSettings { Theme = theme, ReduceMotion = true });
        presentation.Attach(window, (MaterialSurface)window.FindName("Chrome"));
        try
        {
            window.Configure(new OperationCatalog([new ImageEngine()]), []);
            window.AddFiles([path]);
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            themes.ApplyTypography(scale);
            window.ApplyTextScale(scale);
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (((Image)window.FindName("PreviewImage")).Source is null && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            Assert.NotNull(((Image)window.FindName("PreviewImage")).Source);
            window.UpdateLayout();
            var inspector = (FrameworkElement)window.FindName("Inspector");
            var root = (FrameworkElement)window.Content;
            var chrome = (MaterialSurface)window.FindName("Chrome");
            var requested = new { requestedWindowWidthDip = width, requestedWindowHeightDip = height, textScale = scale, materialState = chrome.State.ToString(), captureScope = "WPF-only; native DWM pixels are not captured" };
            string artifact = $"workspace-requested-{width}x{height}-{theme}-text{scale * 100:0}-raster100";
            var capture = VisualArtifacts.Capture(root, artifact, 1, theme, requested);
            // Preserve the actual backdrop without requiring lossless alpha arithmetic at glyph edges.
            // Native WPF captures contain alpha 254 (one unit below opaque) around antialiased text;
            // Interior background holes (alpha 0..253) must still fail. Rounded outer
            // corner pixels are intentionally transparent and are checked separately.
            var pixels = new byte[capture.PixelWidth * capture.PixelHeight * 4];
            capture.CopyPixels(pixels, capture.PixelWidth * 4, 0);
            // Exclude only a fractional final raster row/column, whose coverage can be below one pixel.
            if (chrome.State == BackdropState.Solid)
            {
                double radius = new[] { chrome.CornerRadius.TopLeft, chrome.CornerRadius.TopRight, chrome.CornerRadius.BottomRight, chrome.CornerRadius.BottomLeft }.Max() + 1;
                Assert.True(Enumerable.Range(1, (int)Math.Floor(root.ActualHeight) - 2).All(y =>
                    Enumerable.Range(1, (int)Math.Floor(root.ActualWidth) - 2).All(x =>
                        (x < radius || x >= root.ActualWidth - radius) && (y < radius || y >= root.ActualHeight - radius) ||
                        pixels[(y * capture.PixelWidth + x) * 4 + 3] >= 254)),
                    "Workspace capture lost the actual interior window background.");
                foreach (var (x, y) in new[] { (2, 2), (capture.PixelWidth - 3, 2), (2, capture.PixelHeight - 3), (capture.PixelWidth - 3, capture.PixelHeight - 3) })
                    Assert.Equal(0, pixels[(y * capture.PixelWidth + x) * 4 + 3]);
            }
            if (chrome.State == BackdropState.ConfiguredNativeAcrylic)
            {
                Assert.Equal(0, ((SolidColorBrush)window.Background).Color.A);
                Assert.True(((SolidColorBrush)chrome.EffectiveTint).Color.A < 255);
                int chromePixel = ((int)(root.ActualHeight / 2) * capture.PixelWidth + 10) * 4;
                Assert.InRange(pixels[chromePixel + 3], (byte)1, (byte)253); // Real WPF tint in exposed left chrome.

            }
            Assert.Equal(255, ((SolidColorBrush)((System.Windows.Controls.Border)inspector).Background).Color.A);
            var header = (Grid)window.FindName("WorkspaceHeader");
            VisualArtifacts.AssertInside(header, root);
            foreach (FrameworkElement child in header.Children) VisualArtifacts.AssertInside(child, root);
            VisualArtifacts.AssertInside((FrameworkElement)window.FindName("GesturesEnabled"), root);
            foreach (string name in new[] { "Status", "EngineStatus" })
            {
                var status = (FrameworkElement)window.FindName(name);
                if (status.IsVisible) VisualArtifacts.AssertInside(status, root);
            }
            // The primary action is pinned outside the body viewport, including at minimum size.
            Assert.Same(window.FindName("WorkspaceRoot"), inspector.Parent);
            Assert.True(((System.Windows.Controls.Button)window.FindName("RunButton")).IsEnabled);
            var run = (FrameworkElement)window.FindName("RunButton");
            Assert.True(run.IsVisible);
            VisualArtifacts.AssertInside(run, root);
            var body = (System.Windows.Controls.ScrollViewer)window.FindName("WorkspaceScroll");
            var pinned = run.TransformToAncestor(root).Transform(new System.Windows.Point());
            body.ScrollToEnd();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var position = run.TransformToAncestor(root).Transform(new System.Windows.Point(0, 0));
            Assert.Equal(pinned, position);
            VisualArtifacts.Capture(root, artifact + "-inspector", 1, theme, requested);
            Assert.InRange(position.Y, 0, root.ActualHeight - run.ActualHeight + 1);
            VisualArtifacts.AssertInside(run, root);
        }
        finally { window.Dispose(); window.Close(); themes.ApplyTypography(1); themes.Apply(ThemeChoice.Light, reduceMotion: true); }
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
