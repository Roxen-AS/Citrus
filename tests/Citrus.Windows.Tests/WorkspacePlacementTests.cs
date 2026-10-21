using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WorkspacePlacementTests(DesktopFixture desktop)
{
    [Fact]
    public Task Oversized_initial_workspace_fits_current_work_area_before_becoming_visible() => desktop.RunAsync(async () =>
    {
        Assert.True(NativeMethods.GetCursorPos(out var pointer));
        var work = NativeMethods.WorkArea(pointer);
        double scale = NativeMethods.MonitorScale(pointer);
        using var window = new MainWindow((App)Application.Current)
        {
            Width = work.Width / scale + 200,
            Height = work.Height / scale + 200,
            ShowActivated = false
        };
        nint foreground = GetForegroundWindow();
        bool observedHiddenPlacement = false;
        Rect initial = default;
        window.SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(window).Handle;
            observedHiddenPlacement = !IsWindowVisible(handle);
            Assert.True(GetWindowRect(handle, out initial));
        };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(observedHiddenPlacement);
            Assert.Equal(foreground, GetForegroundWindow());
            AssertInside(initial, work);
            Assert.True(GetWindowRect(new WindowInteropHelper(window).Handle, out var shown));
            AssertInside(shown, work);
            Assert.True(window.ActualWidth <= work.Width / scale * Citrus.Windows.Presentation.CompactWindowSizing.WidthFraction + 1);
            Assert.True(window.ActualHeight <= work.Height / scale * Citrus.Windows.Presentation.CompactWindowSizing.HeightFraction + 1);
            var footer = (FrameworkElement)window.FindName("Status");
            var footerBottom = footer.PointToScreen(new System.Windows.Point(footer.ActualWidth, footer.ActualHeight));
            Assert.InRange(footerBottom.Y, work.Top, work.Bottom);
            Assert.InRange(footerBottom.X, work.Left, work.Right);
            var run = (FrameworkElement)window.FindName("RunButton");
            run.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var actionBottom = run.PointToScreen(new System.Windows.Point(run.ActualWidth, run.ActualHeight));
            Assert.InRange(actionBottom.Y, work.Top, work.Bottom);
            Assert.InRange(actionBottom.X, work.Left, work.Right);

            // Initial placement must not install permanent MaxWidth/MaxHeight or a resize clamp.
            Assert.True(double.IsPositiveInfinity(window.MaxWidth));
            Assert.True(double.IsPositiveInfinity(window.MaxHeight));
            double resizedWidth = Math.Max(window.MinWidth, window.ActualWidth - 40);
            window.Width = resizedWidth;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            double left = window.Left;
            window.Hide(); window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(resizedWidth, window.ActualWidth, 0);
            Assert.Equal(left, window.Left, 0);
        }
        finally { window.Dispose(); window.Close(); }
    });

    [Fact]
    public Task Normal_workspace_request_is_preserved_when_the_monitor_can_fit_it() => desktop.RunAsync(async () =>
    {
        Assert.True(NativeMethods.GetCursorPos(out var pointer));
        var work = NativeMethods.WorkArea(pointer);
        double scale = NativeMethods.MonitorScale(pointer);
        using var window = new MainWindow((App)Application.Current) { ShowActivated = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Math.Min(840, work.Width / scale * Citrus.Windows.Presentation.CompactWindowSizing.WidthFraction), window.ActualWidth, 0);
            Assert.Equal(Math.Min(620, work.Height / scale * Citrus.Windows.Presentation.CompactWindowSizing.HeightFraction), window.ActualHeight, 0);
            Assert.True(GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds));
            AssertInside(bounds, work);
            Assert.InRange(Math.Abs((bounds.Left + bounds.Right) / 2.0 - (work.Left + work.Right) / 2), 0, 2);
            Assert.InRange(Math.Abs((bounds.Top + bounds.Bottom) / 2.0 - (work.Top + work.Bottom) / 2), 0, 2);
        }
        finally { window.Dispose(); window.Close(); }
    });

    private static void AssertInside(Rect bounds, Citrus.Core.ScreenRect work)
    {
        Assert.InRange(bounds.Left, work.Left, work.Right);
        Assert.InRange(bounds.Top, work.Top, work.Bottom);
        Assert.InRange(bounds.Right, work.Left, work.Right);
        Assert.InRange(bounds.Bottom, work.Top, work.Bottom);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
