using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Citrus.Core;
using Citrus.Windows.Presentation;
using Point = System.Windows.Point;
using Window = System.Windows.Window;
using Brushes = System.Windows.Media.Brushes;

namespace Citrus.Windows.Tests;

/// <summary>Only test-owned windows are captured. Real layered WheelWindow/OLE registration;
/// callback admission is synthetic and is explicitly not a real external OLE drag proof.</summary>
internal sealed class GlassOwnedScene : IDisposable
{
    private readonly Window background;
    private readonly string sample = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "citrus-glass-" + Guid.NewGuid().ToString("N") + ".txt");
    public WheelWindow Wheel { get; } = new(new OperationCatalog([]));
    public nint WheelHandle => new WindowInteropHelper(Wheel).Handle;
    public nint BackgroundHandle => new WindowInteropHelper(background).Handle;
    public nint ForegroundBefore { get; private set; }
    public GlassWindowIdentity? OriginalForeground { get; private set; }
    public GlassWindowIdentity? BaselineForeground { get; private set; }
    public bool BackgroundActivationAttempted { get; private set; }
    public bool BackgroundActivationReturned { get; private set; }
    public bool BackgroundActivationSucceeded { get; private set; }
    public double DpiScale => BackdropNativeMethods.GetDpiForWindow(WheelHandle) / 96d;
    public ScreenRect Bounds
    {
        get { Assert.True(BackdropNativeMethods.GetWindowRect(WheelHandle, out var rect)); return rect.ToScreenRect(); }
    }
    public GlassOwnedScene()
    {
        File.WriteAllText(sample, "Owned backdrop scene");
        NativeMethods.GetCursorPos(out var pointer);
        var work = NativeMethods.WorkArea(pointer);
        double scale = NativeMethods.MonitorScale(pointer);
        background = new Window
        {
            Title = "Citrus owned compositor fixture", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            Left = work.Left / scale, Top = work.Top / scale, Width = work.Width / scale, Height = work.Height / scale
        };
        SetPattern(false);
        Wheel.ApplySettings(new CitrusSettings { ReduceMotion = true, SoundEnabled = false });
    }
    public void Show(bool activateOwnedBackground = false)
    {
        nint originalForeground = BackdropNativeMethods.GetForegroundWindow();
        OriginalForeground = GlassNativeDesktopProbe.Describe(originalForeground);
        background.Show();
        if (activateOwnedBackground)
        {
            // One normal activation of our opaque test window is fixture setup, never wheel activation.
            // No force, retry, foreign-window interaction, desktop switch or OS preference change.
            BackgroundActivationAttempted = true;
            BackgroundActivationReturned = background.Activate();
        }
        ForegroundBefore = activateOwnedBackground ? BackdropNativeMethods.GetForegroundWindow() : originalForeground;
        BackgroundActivationSucceeded = BackgroundActivationAttempted && ForegroundBefore == BackgroundHandle;
        BaselineForeground = GlassNativeDesktopProbe.Describe(ForegroundBefore);
        Wheel.ShowAtPointer(OperationMode.Convert);
        var surface = (Canvas)Wheel.FindName("Surface");
        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { sample });
        var args = (System.Windows.DragEventArgs)Activator.CreateInstance(typeof(System.Windows.DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [data, System.Windows.DragDropKeyStates.LeftMouseButton, System.Windows.DragDropEffects.Copy, surface, new Point(184, 184)], null)!;
        args.RoutedEvent = System.Windows.DragDrop.PreviewDragEnterEvent;
        surface.RaiseEvent(args);
        Wheel.UpdateLayout();
    }
    public async Task WaitForPresentationAsync()
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        var presentation = (FrameworkElement)Wheel.FindName("Presentation");
        while ((((TextBlock)Wheel.FindName("Hint")).Text.StartsWith("Preparing", StringComparison.Ordinal) || presentation.Opacity != 1) && deadline.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10);
        Assert.Equal(1, presentation.Opacity);
        Assert.False(((TextBlock)Wheel.FindName("Hint")).Text.StartsWith("Preparing", StringComparison.Ordinal));
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    public void SetShellState(BackdropState state)
    {
        // The neutral-center ellipse remains opaque; only the actual production shell changes material.
        ((MaterialSurface)Wheel.FindName("Shell")).State = state;
    }
    public void SetPattern(bool alternate)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(alternate ? Brushes.DarkRed : Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(alternate ? Brushes.Orange : Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 16))));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16), Stretch = Stretch.None };
        brush.Freeze(); background.Background = brush;
    }
    public GlassCaptureSnapshot ReadCaptureSnapshot(nint companion)
    {
        var bounds = Bounds;
        var work = NativeMethods.WorkArea(new NativeMethods.Point { X = (int)(bounds.Left + bounds.Width / 2), Y = (int)(bounds.Top + bounds.Height / 2) });
        var windows = new List<GlassCaptureWindow>();
        var seen = new HashSet<nint>();
        nint current = GetTopWindow(0);
        // Walk actual visual top-level z order, not WindowFromPoint (which skips disabled/transparent helpers).
        while (current != 0 && windows.Count < 2048 && seen.Add(current))
        {
            bool visible = BackdropNativeMethods.IsWindowVisible(current);
            bool known = BackdropNativeMethods.GetWindowRect(current, out var rectangle);
            windows.Add(new(current.ToInt64(), rectangle.ToScreenRect(), visible, known));
            current = BackdropNativeMethods.GetWindow(current, 2); // GW_HWNDNEXT: top to bottom.
        }
        return GlassNativeDesktopProbe.Complete(new(bounds, work, BackgroundHandle.ToInt64(), WheelHandle.ToInt64(), companion.ToInt64(), windows, current == 0));
    }
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint hwnd);

    public void AssertWheelOwnsClip(nint companion = default)
    {
        string artifacts = Environment.GetEnvironmentVariable("CITRUS_GLASS_ARTIFACTS") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "TestResults", "glass-native");
        GlassCompositorCapture.RequireOwned(this, companion, System.IO.Path.Combine(artifacts, "clip-input.preflight.json"));
        var bounds = Bounds;
        // Every physical pixel strictly inside the inward-rounded native ellipse.
        nint region = BackdropNativeMethods.CreateEllipticRgn((int)Math.Ceiling(8 * DpiScale), (int)Math.Ceiling(8 * DpiScale),
            (int)Math.Floor(360 * DpiScale), (int)Math.Floor(360 * DpiScale));
        try
        {
            for (int y = 0; y < (int)bounds.Height; y++)
                for (int x = 0; x < (int)bounds.Width; x++)
                    if (BackdropNativeMethods.PtInRegion(region, x, y))
                        Assert.Equal(WheelHandle, BackdropNativeMethods.WindowFromPoint(new((int)bounds.Left + x, (int)bounds.Top + y)));
        }
        finally { BackdropNativeMethods.DeleteObject(region); }
    }
    public void Dispose() { Wheel.Close(); background.Close(); File.Delete(sample); }
}
