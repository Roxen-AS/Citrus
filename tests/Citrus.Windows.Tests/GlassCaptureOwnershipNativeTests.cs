using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Citrus.Windows.Presentation;
using Brushes = System.Windows.Media.Brushes;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class GlassCaptureOwnershipNativeTests(DesktopFixture desktop)
{
    [Fact]
    public Task Actual_disabled_transparent_corner_occluder_refuses_capture_before_png_exists() => desktop.RunAsync(async () =>
    {
        using var scene = new GlassOwnedScene(); scene.Show();
        await scene.WaitForPresentationAsync();
        var bounds = scene.Bounds;
        var occluder = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false,
            ShowInTaskbar = false, Topmost = true, Background = Brushes.Magenta,
            Left = bounds.Left / scene.DpiScale, Top = bounds.Top / scene.DpiScale, Width = 12, Height = 12
        };
        string root = Path.Combine(Path.GetTempPath(), "citrus-capture-refusal-" + Guid.NewGuid().ToString("N"));
        string png = Path.Combine(root, "must-not-capture.png");
        try
        {
            occluder.Show();
            nint hwnd = new WindowInteropHelper(occluder).Handle;
            NativeMethods.SetWindowLongPtr(hwnd, -16, NativeMethods.GetWindowLongPtr(hwnd, -16) | (nint)BackdropNativeMethods.WsDisabled);
            NativeMethods.SetWindowLongPtr(hwnd, -20, NativeMethods.GetWindowLongPtr(hwnd, -20) | BackdropNativeMethods.WsExTransparent);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var snapshot = scene.ReadCaptureSnapshot(0);
            Assert.Equal("UnrelatedVisualOccluder", GlassCaptureOwnership.Check(snapshot).Reason);
            Assert.Throws<InvalidOperationException>(() => GlassCompositorCapture.Capture(scene, 0, png));
            Assert.False(File.Exists(png));
            using var metadata = JsonDocument.Parse(File.ReadAllText(png + ".preflight.json"));
            Assert.False(metadata.RootElement.GetProperty("Allowed").GetBoolean());
            Assert.True(metadata.RootElement.GetProperty("CheckedBeforeScreenRead").GetBoolean());
        }
        finally
        {
            occluder.Close();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    });
}
