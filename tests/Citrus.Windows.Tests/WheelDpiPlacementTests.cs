using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Xunit.Abstractions;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class WheelDpiPlacementTests(DesktopFixture desktop, ITestOutputHelper output)
{
    [Fact]
    public Task Placement_uses_the_actual_window_scale() => desktop.RunAsync(async () =>
    {
        var wheel = new WheelWindow(new OperationCatalog([]));
        try
        {
            wheel.ShowAtPointer(OperationMode.Convert);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            wheel.UpdateLayout();
            NativeMethods.GetCursorPos(out var point);
            var handle = new WindowInteropHelper(wheel).Handle;
            Assert.True(BackdropNativeMethods.GetWindowRect(handle, out var rectangle));
            string diagnostic = $"windowDpi={NativeMethods.GetDpiForWindow(handle)} wpfDpi={VisualTreeHelper.GetDpi(wheel).DpiScaleX} monitorScale={NativeMethods.MonitorScale(point)} logical={wheel.Width}x{wheel.Height} physical={rectangle.Right-rectangle.Left}x{rectangle.Bottom-rectangle.Top} transform={((FrameworkElement)wheel.FindName("Presentation")).RenderTransform.Value} work={NativeMethods.WorkArea(point)}";
            output.WriteLine(diagnostic);
            double scale = VisualTreeHelper.GetDpi(wheel).DpiScaleX;
            var expected = WheelGeometry.Place(point.X, point.Y, NativeMethods.WorkArea(point), scale * WheelPresentationMetrics.Scale);
            Assert.True(Math.Abs(wheel.Width - expected.Width / scale) < 1 / scale &&
                Math.Abs(wheel.Height - expected.Height / scale) < 1 / scale, diagnostic);
        }
        finally { wheel.Close(); }
    });
}
