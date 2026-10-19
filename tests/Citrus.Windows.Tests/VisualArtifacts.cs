using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace Citrus.Windows.Tests;

/// <summary>Native WPF evidence only. Raster DPI is an export scale, not a monitor/DPI transition.</summary>
internal static class VisualArtifacts
{
    public static string DirectoryPath => Path.Combine(Environment.CurrentDirectory, "TestResults", "visual");

    public static void Json(string name, object evidence)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, name + ".json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Capture(FrameworkElement root, string name, double scale, ThemeChoice theme, object? context = null)
    {
        root.UpdateLayout();
        int width = (int)Math.Ceiling(root.ActualWidth * scale), height = (int)Math.Ceiling(root.ActualHeight * scale);
        Assert.True(width > 0 && height > 0, "The actual production view must have layout before rendering.");
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        Directory.CreateDirectory(DirectoryPath);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(DirectoryPath, name + ".png"))) encoder.Save(stream);
        var nativeDpi = VisualTreeHelper.GetDpi(root);
        var window = Window.GetWindow(root);
        var screen = window is null ? null : System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        Json(name, new
        {
            capturedUtc = DateTimeOffset.UtcNow, os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription,
            context,
            actualWindowWidthDip = window?.ActualWidth, actualWindowHeightDip = window?.ActualHeight,
            monitor = screen?.DeviceName, monitorBoundsPixels = screen?.Bounds.ToString(), monitorWorkAreaPixels = screen?.WorkingArea.ToString(),
            requestedTheme = theme.ToString(), highContrast = SystemParameters.HighContrast,
            clientAreaAnimation = SystemParameters.ClientAreaAnimation,
            themeInterpretation = SystemParameters.HighContrast ? "OS high contrast overrides requested palette" : "Production ThemeManager palette",
            dpiEvidence = "RenderTargetBitmap raster scale only; no monitor DPI or OS settings changed",
            rasterScale = scale, rasterDpi = 96 * scale, pixelWidth = width, pixelHeight = height,
            layoutWidthDip = root.ActualWidth, layoutHeightDip = root.ActualHeight,
            actualVisualDpiX = nativeDpi.PixelsPerInchX, actualVisualDpiY = nativeDpi.PixelsPerInchY,
            inputEvidence = "Synthetic WPF routed callbacks; not Explorer or native OLE initiation"
        });
        // A blank/transparent image cannot masquerade as visual evidence.
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        Assert.True(Enumerable.Range(0, width * height).Any(i => pixels[i * 4 + 3] > 16), "Rendered view has no visible pixels.");
    }

    public static void AssertInside(FrameworkElement element, FrameworkElement root)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(new Point(), element.RenderSize));
        Assert.True(bounds.Width > 0 && bounds.Height > 0, $"{element.Name} has no rendered size: {bounds}");
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1,
            $"{element.Name} {bounds} exceeds root {root.ActualWidth} × {root.ActualHeight} DIP.");
        // A button inside the window can still be hidden behind a scroll viewport.
        for (var ancestor = VisualTreeHelper.GetParent(element); ancestor is not null && ancestor != root; ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not FrameworkElement { ClipToBounds: true } viewport) continue;
            var visible = element.TransformToAncestor(viewport).TransformBounds(new Rect(new Point(), element.RenderSize));
            Assert.True(visible.Left >= -1 && visible.Top >= -1 && visible.Right <= viewport.ActualWidth + 1 && visible.Bottom <= viewport.ActualHeight + 1,
                $"{element.Name} {visible} is clipped by {viewport.GetType().Name} {viewport.ActualWidth} × {viewport.ActualHeight} DIP.");
        }
    }
}
