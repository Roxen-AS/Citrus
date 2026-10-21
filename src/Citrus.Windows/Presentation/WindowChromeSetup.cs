using System.Windows.Shell;
using System.Windows.Media;

namespace Citrus.Windows.Presentation;

internal static class WindowChromeSetup
{
    internal static void Apply(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        var chrome = new WindowChrome
        {
            CaptionHeight = 0, GlassFrameThickness = new Thickness(0),
            // At radius28 the visible diagonal starts8.2 DIP inward. Keep a usable
            // corner target, with edge scrollbars explicitly exempted in their style.
            ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.NoResize ? 0 : 10),
            UseAeroCaptionButtons = false
        };
        if (window.Content is MaterialSurface surface)
        {
            // Per-pixel alpha clips the complete native output, not only the WPF child.
            // A DWM/accent backdrop paints beyond an HRGN and produces a second square corner.
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
            chrome.CornerRadius = new CornerRadius(0); // One contour: MaterialSurface's alpha edge.
            window.StateChanged += (_, _) => surface.CoerceValue(MaterialSurface.CornerRadiusProperty);
        }
        WindowChrome.SetWindowChrome(window, chrome);
        window.SourceInitialized += (_, _) =>
        {
            if (window.Content is MaterialSurface root) root.CoerceValue(MaterialSurface.CornerRadiusProperty);
            CompactWindowSizing.ApplyInitial(window);
        };
    }
}
