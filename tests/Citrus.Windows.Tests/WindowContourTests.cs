using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Conversion.Editing.Media;
using Citrus.Core;
using Citrus.Windows.Editors;
using Citrus.Windows.Editors.Media;
using Citrus.Windows.Presentation;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WindowContourTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(0x1A)]
    [InlineData(0x31A)]
    [InlineData(0x31E)]
    public Task System_changes_preserve_transparent_native_corner_pixels(int message) => desktop.RunAsync(() =>
    {
        var window = new Window { Content = new MaterialSurface(), Width = 420, Height = 564, ShowActivated = false };
        WindowChromeSetup.Apply(window);
        using var controller = new WindowBackdropController(); controller.Attach(window);
        try
        {
            window.Show(); var hwnd = new WindowInteropHelper(window).Handle;
            SendMessage(hwnd, message, 0, 0);
            Assert.Equal(0, HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor.A);
            controller.Apply(BackdropControllerTests.Eligible, false);
            Assert.Equal(0, HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor.A);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Material_windows_keep_native_pixels_clear_outside_their_rounded_content() => desktop.RunAsync(() =>
    {
        var window = new Window { Content = new MaterialSurface(), Width = 420, Height = 564, ShowActivated = false };
        WindowChromeSetup.Apply(window);
        using var controller = new WindowBackdropController(); controller.Attach(window);
        try
        {
            window.Show();
            var hwnd = new WindowInteropHelper(window).Handle;
            Assert.NotEqual(0, BackdropNativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64() & BackdropNativeMethods.WsExLayered);
            Assert.False(controller.Apply(BackdropControllerTests.Eligible, false).IsNative);
            Assert.Equal(0, HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor.A);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // A square child used to overwrite the curved material. These pixels must stay outside,
    // even after resize and when rendering at fractional display scales.
    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public Task Child_paint_is_clipped_to_all_four_material_corners(double scale) => desktop.RunAsync(() =>
    {
        var surface = new MaterialSurface { CornerRadius = new(28), Child = new Border { Background = Brushes.Lime } };
        foreach (var size in new[] { new Size(200, 120), new Size(320, 220) })
        {
            surface.Measure(size); surface.Arrange(new Rect(size)); surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            foreach (var point in new[] { new Point(2, 2), new Point(size.Width - 3, 2), new Point(2, size.Height - 3), new Point(size.Width - 3, size.Height - 3) })
                Assert.Equal(0, Pixel(bitmap, (int)(point.X * scale), (int)(point.Y * scale)).A);
            Assert.Equal(Colors.Lime, Pixel(bitmap, (int)(size.Width * scale / 2), (int)(size.Height * scale / 2)));
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Native_alpha_follows_content_radius_through_material_switch_and_resize() => desktop.RunAsync(async () =>
    {
        var surface = new MaterialSurface { CornerRadius = new(28), Child = new Border { Background = Brushes.Lime } };
        var window = new Window { Content = surface, Width = 420, Height = 564, ShowActivated = false };
        WindowChromeSetup.Apply(window);
        using var controller = new WindowBackdropController(); controller.Attach(window);
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            AssertCornerRegion(window, false);
            controller.Apply(BackdropControllerTests.Eligible, false);
            AssertCornerRegion(window, false);
            controller.Apply(BackdropControllerTests.Eligible with { RequestNativeAcrylic = false }, false);
            surface.CornerRadius = new(40); window.Width = 500; window.Height = 620;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            AssertCornerRegion(window, false);
            surface.CornerRadius = new(8);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            AssertCornerRegion(window, true);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Maximizing_fills_screen_corners_and_restoring_recovers_the_original_curve() => desktop.RunAsync(async () =>
    {
        var surface = new MaterialSurface { CornerRadius = new(28), Child = new Border { Background = Brushes.Lime } };
        var window = new Window { Content = surface, Width = 420, Height = 564, ShowActivated = false };
        WindowChromeSetup.Apply(window);
        try
        {
            window.Show(); window.WindowState = WindowState.Maximized;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(255, Pixel(VisualArtifacts.Render(surface, 1), 2, 2).A);
            window.WindowState = WindowState.Normal;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, Pixel(VisualArtifacts.Render(surface, 1), 2, 2).A);
            AssertCornerRegion(window, false);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Rounded_resize_corner_and_adjacent_scrollbar_keep_their_distinct_hit_targets() => desktop.RunAsync(async () =>
    {
        var bar = new System.Windows.Controls.Primitives.ScrollBar { HorizontalAlignment = HorizontalAlignment.Right, Minimum = 0, Maximum = 100, ViewportSize = 20, Margin = new Thickness(0, 70, 0, 66) };
        var surface = new MaterialSurface { CornerRadius = new(28), Child = bar };
        var window = new Window { Content = surface, Width = 420, Height = 564, ShowActivated = false };
        WindowChromeSetup.Apply(window);
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var hwnd = new WindowInteropHelper(window).Handle;
            var region = BackdropNativeMethods.CreateRectRgn(0, 0, 0, 0);
            try
            {
                Assert.NotEqual(0, BackdropNativeMethods.GetWindowRgn(hwnd, region));
                double dpi = BackdropNativeMethods.GetDpiForWindow(hwnd) / 96d;
                foreach (var (point, expected) in new[] { (new Point(9, 9), 13), (new Point(surface.ActualWidth - 9, 9), 14),
                    (new Point(9, surface.ActualHeight - 9), 16), (new Point(surface.ActualWidth - 9, surface.ActualHeight - 9), 17) })
                {
                    Assert.True(BackdropNativeMethods.PtInRegion(region, (int)(point.X * dpi), (int)(point.Y * dpi)));
                    Assert.Equal(expected, Hit(point)); // Only actual visible region points qualify as resize targets.
                }
            }
            finally { BackdropNativeMethods.DeleteObject(region); }
            var thumb = (System.Windows.Controls.Primitives.Thumb)((System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar)).Thumb;
            Assert.Equal(1, Hit(thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), surface))); // HTCLIENT: scroll thumb must not resize.
        }
        finally { window.Close(); }
        int Hit(Point point)
        {
            var pixel = surface.PointToScreen(point);
            nint packed = (nint)(((int)pixel.Y << 16) | ((int)pixel.X & 0xffff));
            return (int)SendMessage(new WindowInteropHelper(window).Handle, 0x84, 0, packed);
        }
    });

    [Fact]
    public Task Layered_native_target_stays_clear_while_material_is_opaque() => desktop.RunAsync(() =>
    {
        using var theme = new ThemeManager(Application.Current);
        theme.Apply(ThemeChoice.Dark, true);
        var window = new Window { Content = new MaterialSurface(), Width = 420, Height = 564, ShowActivated = false };
        WindowChromeSetup.Apply(window);
        using var controller = new WindowBackdropController(); controller.Attach(window);
        try
        {
            window.Show();
            controller.Apply(BackdropControllerTests.Eligible with { RequestNativeAcrylic = false }, true);
            Assert.Equal(0, HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!.CompositionTarget.BackgroundColor.A);
            Assert.Equal(((SolidColorBrush)window.FindResource("WindowBackground")).Color,
                ((SolidColorBrush)((MaterialSurface)window.Content).EffectiveTint).Color);
        }
        finally { window.Close(); theme.Apply(ThemeChoice.Light, true); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("home")]
    [InlineData("settings")]
    [InlineData("options")]
    [InlineData("shelf")]
    [InlineData("image")]
    [InlineData("video")]
    [InlineData("toast")]
    [InlineData("demo")]
    public Task Every_program_window_keeps_ancestor_paint_outside_the_material(string kind) => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        using var services = new WindowPresentationService(Application.Current, theme, () => new CitrusSettings { ReduceTransparency = true });
        var window = Create(kind); window.ShowActivated = false;
        var surface = Assert.IsType<MaterialSurface>(window.Content);
        try
        {
            services.Attach(window, surface);
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            foreach (var choice in new[] { ThemeChoice.Light, ThemeChoice.Dark })
            foreach (bool highContrast in new[] { false, true })
            {
                theme.ApplyPalette(choice, true, highContrast);
                var bitmap = VisualArtifacts.Render(surface, 1);
                Assert.Equal(0, Pixel(bitmap, 2, 2).A);
                Assert.Equal(0, Pixel(bitmap, bitmap.PixelWidth - 3, bitmap.PixelHeight - 3).A);
                Assert.Equal(255, ((SolidColorBrush)surface.EffectiveTint).Color.A);
                AssertCornerRegion(window, false, 2);
                if (!highContrast) VisualArtifacts.Capture(surface, $"contour-{kind}-{choice}", 1, choice);
            }
        }
        finally { if (window is IDisposable disposable) disposable.Dispose(); window.Close(); theme.Apply(ThemeChoice.Light, true); }
    });

    private static Window Create(string kind) => kind switch
    {
        "home" => new MainWindow((App)Application.Current, startInLauncher: true),
        "settings" => new SettingsWindow(new(), _ => Task.CompletedTask),
        "options" => new ToolOptionsWindow(new(new("image.resize", "Resize", OperationMode.Tools, ".png", false, true), ["sample.png"], new(), null, [])),
        "shelf" => new ShelfWindow(),
        "image" => new EditorHostWindow(new(new())),
        "video" => new VideoCropWindow(new(new MediaProcess(System.IO.Path.GetFullPath("unused-ffmpeg"), System.IO.Path.GetFullPath("unused-ffprobe")), new(new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), false), new())),
        "toast" => new JobToastWindow(() => { }, _ => Task.CompletedTask, () => true),
        "demo" => new WheelDemoWindow(),
        _ => throw new ArgumentException(kind)
    };
    private static void AssertCornerRegion(Window window, bool includesNearCorner, double offset = 7)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (window.AllowsTransparency)
        {
            Assert.NotEqual(0, BackdropNativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64() & BackdropNativeMethods.WsExLayered);
            Assert.Equal(0, HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor.A);
            var bitmap = VisualArtifacts.Render((FrameworkElement)window.Content, 1);
            int near = (int)offset;
            foreach (var point in new[] { (near, near), (bitmap.PixelWidth - near - 1, near), (near, bitmap.PixelHeight - near - 1), (bitmap.PixelWidth - near - 1, bitmap.PixelHeight - near - 1) })
                Assert.Equal(includesNearCorner, Pixel(bitmap, point.Item1, point.Item2).A > 0);
            return;
        }
        var region = BackdropNativeMethods.CreateRectRgn(0, 0, 0, 0);
        try
        {
            Assert.NotEqual(0, BackdropNativeMethods.GetWindowRgn(hwnd, region));
            double dpi = BackdropNativeMethods.GetDpiForWindow(hwnd) / 96d;
            int near = (int)(offset * dpi);
            Assert.NotEqual(0, BackdropNativeMethods.GetRgnBox(region, out var bounds));
            foreach (var point in new[] { (near, near), (bounds.Right - near - 1, near), (near, bounds.Bottom - near - 1), (bounds.Right - near - 1, bounds.Bottom - near - 1) })
                Assert.Equal(includesNearCorner, BackdropNativeMethods.PtInRegion(region, point.Item1, point.Item2));
            Assert.True(BackdropNativeMethods.PtInRegion(region, (int)(60 * dpi), (int)(60 * dpi)));
        }
        finally { BackdropNativeMethods.DeleteObject(region); }
    }
    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        byte[] pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
}
