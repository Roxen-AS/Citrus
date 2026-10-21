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
    private readonly ScreenRect backgroundBounds;
    private readonly Window? ordinaryTarget;
    // Snapshot.Wheel is the existing guarded target slot; ordinary mode supplies this owned HWND.
    public nint CaptureTargetHandle => ordinaryTarget is null ? WheelHandle : new WindowInteropHelper(ordinaryTarget).Handle;
    public string CaptureTargetKind => ordinaryTarget?.GetType().Name ?? nameof(WheelWindow);
    private readonly string sample = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "citrus-glass-" + Guid.NewGuid().ToString("N") + ".txt");
    public WheelWindow Wheel { get; } = new(new OperationCatalog([]));
    public nint WheelHandle => new WindowInteropHelper(Wheel).Handle;
    public nint BackgroundHandle => new WindowInteropHelper(background).Handle;
    internal string SamplePath => sample;
    public nint ForegroundBefore { get; private set; }
    public GlassWindowIdentity? OriginalForeground { get; private set; }
    public GlassWindowIdentity? BaselineForeground { get; private set; }
    public bool BackgroundActivationAttempted { get; private set; }
    public bool BackgroundActivationReturned { get; private set; }
    public bool BackgroundActivationSucceeded { get; private set; }
    public double DpiScale => BackdropNativeMethods.GetDpiForWindow(CaptureTargetHandle) / 96d;
    public ScreenRect Bounds
    {
        get { Assert.True(BackdropNativeMethods.GetWindowRect(CaptureTargetHandle, out var rect)); return rect.ToScreenRect(); }
    }
    public GlassOwnedScene(Window? ordinaryTarget = null, NativeMethods.Point? fixturePoint = null)
    {
        this.ordinaryTarget = ordinaryTarget;
        File.WriteAllText(sample, "Owned backdrop scene");
        NativeMethods.Point pointer;
        if (fixturePoint is { } requested) pointer = requested;
        else NativeMethods.GetCursorPos(out pointer);
        var work = NativeMethods.WorkArea(pointer);
        backgroundBounds = work;
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
        // WPF Left/Top use its device transform, which can differ across monitors.
        // Establish physical fixture coverage after Show has applied logical placement.
        Assert.True(NativeMethods.SetWindowPos(BackgroundHandle, new nint(-1), (int)backgroundBounds.Left, (int)backgroundBounds.Top,
            (int)backgroundBounds.Width, (int)backgroundBounds.Height, 0x10));
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
        if (ordinaryTarget is not null)
        {
            ordinaryTarget.ShowActivated = false;
            ordinaryTarget.Topmost = true;
            ordinaryTarget.Show();
            ordinaryTarget.UpdateLayout();
            return;
        }
        ConfirmWheel();
    }
    public void ConfirmWheel()
    {
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
    public void SetBroadPattern(bool alternate)
    {
        // Broad spatial changes are distinct from the existing 16-DIP fine stripe control.
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(alternate ? Brushes.White : Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 256, 256))));
        drawing.Children.Add(new GeometryDrawing(alternate ? Brushes.Black : Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 128, 256))));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 256, 256), Stretch = Stretch.None };
        brush.Freeze(); background.Background = brush;
    }
    public void SetPattern(bool alternate)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(alternate ? Brushes.DarkRed : Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(alternate ? Brushes.Orange : Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 16))));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16), Stretch = Stretch.None };
        brush.Freeze(); background.Background = brush;
    }
    public void SetSolidPattern(bool light) => background.Background = light ? Brushes.White : Brushes.Black;

    public void SetStudioCheckerboard(bool alternate = false, double tileDip = 20, Point? cellCenterInTargetDip = null)
    {
        if (!double.IsFinite(tileDip) || tileDip <= 0) throw new ArgumentOutOfRangeException(nameof(tileDip));
        // The default remains Studio's generated white/.75 sRGB checker every20DIP.
        // An owned native production proof may choose a coarser fixture and align its
        // first cell to a known target point, without changing Studio calibration.
        Point origin = default;
        if (cellCenterInTargetDip is { } center)
        {
            var target = ordinaryTarget ?? Wheel;
            FrameworkElement coordinates = target is WheelWindow wheelTarget
                ? (FrameworkElement)wheelTarget.FindName("Surface") : target;
            Point screenOrigin = coordinates.PointToScreen(default);
            Point screenRight = coordinates.PointToScreen(new Point(368, 0));
            double geometryScale = (screenRight.X - screenOrigin.X) / 368;
            double backgroundScale = VisualTreeHelper.GetDpi(background).DpiScaleX;
            // The wheel's fixed canonical surface may be displayed through a
            // smaller Viewbox. Keep fixture cells at the same canonical scale as
            // the optical samples, rather than using the monitor DPI alone.
            tileDip *= geometryScale / backgroundScale;
            var backgroundCenter = background.PointFromScreen(coordinates.PointToScreen(center));
            origin = new Point(backgroundCenter.X - tileDip / 2, backgroundCenter.Y - tileDip / 2);
        }
        double period = tileDip * 2;
        var gray = new SolidColorBrush(System.Windows.Media.Color.FromRgb(191, 191, 191)); gray.Freeze();
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(alternate ? gray : Brushes.White, null, new RectangleGeometry(new Rect(0, 0, period, period))));
        drawing.Children.Add(new GeometryDrawing(alternate ? Brushes.White : gray, null, new RectangleGeometry(new Rect(0, 0, tileDip, tileDip))));
        drawing.Children.Add(new GeometryDrawing(alternate ? Brushes.White : gray, null, new RectangleGeometry(new Rect(tileDip, tileDip, tileDip, tileDip))));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(origin.X, origin.Y, period, period), Stretch = Stretch.None
        };
        brush.Freeze(); background.Background = brush;
    }

    public void CenterCaptureTargetWithPadding(int paddingPixels)
    {
        if (paddingPixels < 0) throw new ArgumentOutOfRangeException(nameof(paddingPixels));
        var bounds = Bounds;
        Assert.True(bounds.Width + paddingPixels * 2 <= backgroundBounds.Width &&
            bounds.Height + paddingPixels * 2 <= backgroundBounds.Height,
            "The owned workarea fixture cannot contain the full padded optical crop.");
        int left = (int)(backgroundBounds.Left + (backgroundBounds.Width - bounds.Width) / 2);
        int top = (int)(backgroundBounds.Top + (backgroundBounds.Height - bounds.Height) / 2);
        Assert.True(NativeMethods.SetWindowPos(CaptureTargetHandle, new nint(-1), left, top,
            (int)bounds.Width, (int)bounds.Height, 0x10));
    }

    public async Task<int> CenterCaptureTargetWithPaddingAsync(Func<double, int> paddingFromDpi)
    {
        ArgumentNullException.ThrowIfNull(paddingFromDpi);
        var target = ordinaryTarget ?? Wheel;
        var center = new NativeMethods.Point
        {
            X = (int)(backgroundBounds.Left + backgroundBounds.Width / 2),
            Y = (int)(backgroundBounds.Top + backgroundBounds.Height / 2)
        };
        nint fixtureMonitor = NativeMethods.MonitorFromPoint(center, 2);
        var before = Bounds;
        // Move the existing physical rectangle first. WPF may have created this ordinary
        // window on another monitor; its initial size and padding then use that monitor's DPI.
        Assert.True(NativeMethods.SetWindowPos(CaptureTargetHandle, new nint(-1),
            (int)(center.X - before.Width / 2), (int)(center.Y - before.Height / 2), 0, 0, 0x11));
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        (uint Dpi, double Width, double Height)? previous = null;
        int stable = 0;
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await Task.Delay(25);
            target.UpdateLayout();
            var bounds = Bounds;
            uint nativeDpi = NativeMethods.GetDpiForWindow(CaptureTargetHandle);
            uint fixtureDpi = NativeMethods.GetDpiForWindow(BackgroundHandle);
            double wpfScale = VisualTreeHelper.GetDpi(target).DpiScaleX;
            var actualMonitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point
            {
                X = (int)(bounds.Left + bounds.Width / 2), Y = (int)(bounds.Top + bounds.Height / 2)
            }, 2);
            var snapshot = (nativeDpi, bounds.Width, bounds.Height);
            bool aligned = actualMonitor == fixtureMonitor && nativeDpi != 0 && nativeDpi == fixtureDpi &&
                Math.Abs(wpfScale - nativeDpi / 96d) < .001;
            stable = aligned && previous == snapshot ? stable + 1 : 0;
            previous = snapshot;
            if (stable >= 2) break;
        }
        Assert.True(stable >= 2, "The owned target did not settle on the fixture monitor's actual native and WPF DPI.");
        int padding = paddingFromDpi(DpiScale);
        CenterCaptureTargetWithPadding(padding);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        target.UpdateLayout();
        int settledPadding = paddingFromDpi(DpiScale);
        // Preserve the strict full-padding containment check after the final native placement.
        CenterCaptureTargetWithPadding(settledPadding);
        return settledPadding;
    }

    public GlassCaptureSnapshot ReadCaptureSnapshot(nint companion, int paddingPixels = 0)
        => GlassNativeDesktopProbe.Complete(CollectCaptureScene(companion, paddingPixels));

    public async Task<GlassCaptureSnapshot> ReadCaptureSnapshotAsync(nint companion, int paddingPixels = 0)
    {
        (ordinaryTarget ?? Wheel).Dispatcher.VerifyAccess();
        var initial = CollectCaptureScene(companion, paddingPixels);
        // Every physical pixel and the same desktop/session checks remain
        // mandatory. Only their scheduling changes: the owned WPF windows keep
        // processing messages instead of becoming DWM ghost windows mid-walk.
        var checkedScene = await Task.Run(() => GlassNativeDesktopProbe.Complete(initial));
        var final = CollectCaptureScene(companion, paddingPixels);
        if (initial.Crop != final.Crop || initial.SurfaceCrop != final.SurfaceCrop || initial.WorkArea != final.WorkArea ||
            initial.Background != final.Background || initial.Wheel != final.Wheel || initial.Companion != final.Companion ||
            initial.DragCapture != final.DragCapture)
            return checkedScene with
            {
                Input = new(checkedScene.Input?.CheckedPixels ?? 0, (long)(initial.Crop.Width * initial.Crop.Height), false,
                    Failure: "SceneChangedDuringPreflight")
            };
        // Re-read visual z order after the awaited input walk. This retains the
        // full visual-occluder policy for windows that appeared while it ran.
        return checkedScene with { Windows = final.Windows, Complete = final.Complete, DragCaptureIdentity = final.DragCaptureIdentity };
    }

    private GlassCaptureSnapshot CollectCaptureScene(nint companion, int paddingPixels)
    {
        if (paddingPixels is < 0 or > 8192) throw new ArgumentOutOfRangeException(nameof(paddingPixels));
        var bounds = Bounds;
        var crop = paddingPixels == 0 ? bounds : new ScreenRect(bounds.Left - paddingPixels,
            bounds.Top - paddingPixels, bounds.Width + 2 * paddingPixels, bounds.Height + 2 * paddingPixels);
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
        nint capture = (ordinaryTarget as WheelWindow ?? Wheel).DragCaptureHandle;
        return new(crop, work, BackgroundHandle.ToInt64(), CaptureTargetHandle.ToInt64(), companion.ToInt64(), windows, current == 0,
            DragCapture: capture.ToInt64(), DragCaptureIdentity: capture == 0 ? null : GlassNativeDesktopProbe.Describe(capture),
            SurfaceCrop: paddingPixels == 0 ? null : bounds);
    }
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint hwnd);

    public void AssertWheelOwnsClip(nint companion = default)
    {
        string artifacts = Environment.GetEnvironmentVariable("CITRUS_GLASS_ARTIFACTS") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "TestResults", "glass-native");
        var snapshot = GlassCompositorCapture.RequireOwned(this, companion, System.IO.Path.Combine(artifacts, "clip-input.preflight.json"));
        nint capture = new(snapshot.DragCapture);
        Assert.NotEqual(nint.Zero, capture);
        Assert.Equal(capture, Wheel.DragCaptureHandle);
        var bounds = Bounds;
        var surface = (FrameworkElement)Wheel.FindName("Surface");
        Point topLeft = surface.PointToScreen(new Point(8, 8));
        Point bottomRight = surface.PointToScreen(new Point(360, 360));
        // The canonical surface is scaled by its Viewbox as well as monitor DPI.
        // Every physical pixel strictly inside the inward-rounded native ellipse.
        nint region = BackdropNativeMethods.CreateEllipticRgn(
            (int)Math.Ceiling(topLeft.X - bounds.Left), (int)Math.Ceiling(topLeft.Y - bounds.Top),
            (int)Math.Floor(bottomRight.X - bounds.Left), (int)Math.Floor(bottomRight.Y - bounds.Top));
        try
        {
            for (int y = 0; y < (int)bounds.Height; y++)
                for (int x = 0; x < (int)bounds.Width; x++)
                    if (BackdropNativeMethods.PtInRegion(region, x, y))
                    {
                        nint actual = BackdropNativeMethods.WindowFromPoint(new((int)bounds.Left + x, (int)bounds.Top + y));
                        // Antialiased, alpha-quantized edge pixels can fall through
                        // to this same admitted wheel's registered OLE target. It
                        // forwards screen coordinates through Surface.PointFromScreen.
                        Assert.True(actual == WheelHandle || actual == capture,
                            $"Wheel clip input at ({x},{y}) resolved {actual}, expected wheel {WheelHandle} or admitted capture {capture}; background {BackgroundHandle}, companion {companion}; transformed contour {topLeft}..{bottomRight}, native bounds {bounds}.");
                    }
            Assert.Equal(capture, Wheel.DragCaptureHandle);
        }
        finally { BackdropNativeMethods.DeleteObject(region); }
    }
    public void Dispose() { Wheel.Close(); ordinaryTarget?.Close(); background.Close(); File.Delete(sample); }
}
