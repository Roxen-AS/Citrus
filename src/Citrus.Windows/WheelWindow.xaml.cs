using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Citrus.Core;
using ShapePath = System.Windows.Shapes.Path;

namespace Citrus.Windows;

public sealed record OperationDrop(OperationDescriptor Operation, IReadOnlyList<string> Paths);

public partial class WheelWindow : Window
{
    private readonly OperationCatalog catalog;
    private readonly WheelSession session = new();
    private readonly DispatcherTimer timer;
    private WheelPaging paging = new();
    private IReadOnlyList<OperationDescriptor> operations = [];
    private string[] paths = [];
    private OperationMode mode;
    private long generation;
    private int selected = -1, navigation;
    private bool armed;
    private NativeMethods.Point anchor;
    private nint handle;
    public event Action<OperationDrop>? OperationDropped;

    public WheelWindow(OperationCatalog catalog)
    {
        this.catalog = catalog;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, -20);
            NativeMethods.SetWindowLongPtr(handle, -20, style | 0x08000000 | 0x00000080); // NOACTIVATE | TOOLWINDOW; preserve existing styles.
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        };
        Surface.PreviewDragEnter += OnPreviewDragOver;
        Surface.PreviewDragOver += OnPreviewDragOver;
        Surface.PreviewDrop += OnPreviewDrop;
        Surface.PreviewDragLeave += (_, e) => { e.Handled = true; Dismiss(); };
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, Tick, Dispatcher);
        timer.Stop();
    }

    public void ShowAtPointer(OperationMode requestedMode)
    {
        Dispatcher.VerifyAccess();
        mode = requestedMode;
        paths = [];
        operations = [];
        paging = new();
        selected = -1;
        navigation = 0;
        armed = false;
        NativeMethods.GetCursorPos(out anchor);
        new WindowInteropHelper(this).EnsureHandle();
        Place(NativeMethods.MonitorScale(anchor));
        Show(); // ShowActivated=false; no Focus, Activate or mouse capture.
        Place(Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0);
        var initial = Surface.PointFromScreen(new Point(anchor.X, anchor.Y));
        generation = session.Begin(Environment.TickCount64, initial.X, initial.Y);
        ModeLabel.Text = mode.ToString().ToUpperInvariant();
        Badge.Text = "Drag a file here";
        Hint.Text = "Hold · glide · release";
        RenderSectors();
        timer.Start();
    }

    private void Place(double scale)
    {
        var bounds = WheelGeometry.Place(anchor.X, anchor.Y, NativeMethods.WorkArea(anchor), scale);
        NativeMethods.SetWindowPos(handle, new nint(-1), (int)bounds.Left, (int)bounds.Top,
            (int)bounds.Width, (int)bounds.Height, 0x10); // SWP_NOACTIVATE
    }

    private nint WindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // MA_NOACTIVATE
        if (message == 0x02E0) // WM_DPICHANGED: WPF updates its transforms; clamp native placement afterward.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (IsVisible) Place(Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0); }));
        return 0;
    }

    public void Dismiss()
    {
        timer.Stop();
        paths = [];
        operations = [];
        selected = -1;
        navigation = 0;
        Hide();
    }

    private bool ReadPayload(DragEventArgs e, out string[] payload)
    {
        Dispatcher.VerifyAccess(); // Never allow COM IDataObject or DragEventArgs to leave this callback/STA.
        payload = [];
        if ((e.AllowedEffects & DragDropEffects.Copy) == 0) return false;
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop, false) || e.Data.GetData(DataFormats.FileDrop, false) is not string[] raw) return false;
            payload = raw.ToArray();
            return DropValidation.Accepts(payload, mode);
        }
        catch (Exception error) when (error is COMException or IOException or ArgumentException or InvalidOperationException) { return false; }
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!ReadPayload(e, out var payload)) { Dismiss(); return; }
        if (!payload.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
        {
            paths = payload;
            operations = catalog.GetOperations(paths, mode);
            paging = new();
            if (operations.Count == 0) { Badge.Text = "No supported operations"; Hint.Text = "Release to cancel"; RenderSectors(); return; }
            session.ConfirmPayload();
            Badge.Text = paths.Length == 1 ? Path.GetFileName(paths[0]) : $"{paths.Length} files";
            RenderSectors();
        }
        var point = e.GetPosition(Surface);
        armed = session.ObservePointer(point.X, point.Y);
        navigation = armed ? NavigationAt(point) : 0;
        int next = armed && navigation == 0 ? WheelGeometry.HitTest(point.X, point.Y, Math.Min(8, operations.Count - paging.Page * 8)) : -1;
        if (next != selected) { selected = next; RenderSectors(); }
        if (selected >= 0 || navigation != 0) e.Effects = DragDropEffects.Copy;
    }

    private int NavigationAt(Point point)
    {
        if (operations.Count <= 8 || point.Y is < 328 or > 362) return 0;
        if (point.X is >= 18 and <= 74 && paging.Page > 0) return -1;
        if (point.X is >= 262 and <= 318 && (paging.Page + 1) * 8 < operations.Count) return 1;
        return 0;
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        OperationDrop? accepted = null;
        if (ReadPayload(e, out var payload) && payload.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
        {
            var point = e.GetPosition(Surface);
            int index = armed && NavigationAt(point) == 0 ? WheelGeometry.HitTest(point.X, point.Y, Math.Min(8, operations.Count - paging.Page * 8)) : -1;
            if (index >= 0)
            {
                var operation = operations[paging.Page * 8 + index];
                accepted = new(operation, Array.AsReadOnly(payload));
                e.Effects = DragDropEffects.Copy;
            }
        }
        Dismiss();
        // OLE returns before any option dialog, jobs, clipboard work or other application callbacks.
        if (accepted is { } drop) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => OperationDropped?.Invoke(drop)));
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (session.ShouldExpire(generation, Environment.TickCount64)) { Dismiss(); return; }
        int old = paging.Page;
        paging.UpdateHover(navigation, Environment.TickCount64, operations.Count);
        if (old != paging.Page) { selected = -1; RenderSectors(); }
    }

    private void RenderSectors()
    {
        Sectors.Children.Clear();
        var items = operations.Skip(paging.Page * 8).Take(8).ToArray();
        for (int i = 0; i < items.Length; i++)
        {
            double start = i * 45 - 21, end = i * 45 + 21;
            Point At(double radius, double angle) => new(WheelGeometry.CenterX + radius * Math.Sin(angle * Math.PI / 180), WheelGeometry.CenterY - radius * Math.Cos(angle * Math.PI / 180));
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(At(70, start), true, true);
                context.LineTo(At(146, start), true, false);
                context.ArcTo(At(146, end), new Size(146, 146), 0, false, SweepDirection.Clockwise, true, false);
                context.LineTo(At(70, end), true, false);
                context.ArcTo(At(70, start), new Size(70, 70), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            Sectors.Children.Add(new ShapePath { Data = geometry, Fill = Brush(i == selected ? "#F79545" : "#353A40"), Stroke = Brush(i == selected ? "#FFD1A3" : "#4B5157"), StrokeThickness = 1 });
            var label = new TextBlock { Text = items[i].Label, Width = 73, FontSize = items[i].Label.Length > 9 ? 10 : 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Brush(i == selected ? "#241A12" : "#F3EFE9") };
            var center = At(108, i * 45);
            Canvas.SetLeft(label, center.X - 36.5);
            Canvas.SetTop(label, center.Y - 8);
            Sectors.Children.Add(label);
        }
        Previous.Visibility = Next.Visibility = operations.Count > 8 ? Visibility.Visible : Visibility.Hidden;
        Previous.Opacity = paging.Page == 0 ? 0.35 : 1;
        Next.Opacity = (paging.Page + 1) * 8 >= operations.Count ? 0.35 : 1;
        if (operations.Count > 8) Hint.Text = $"{paging.Page + 1} / {(operations.Count + 7) / 8} · hover arrows";
    }

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
