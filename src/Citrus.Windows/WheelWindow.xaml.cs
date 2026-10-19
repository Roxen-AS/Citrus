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
    private IReadOnlyList<string> paths => session.Paths;
    private OperationMode mode => session.Mode;
    private long generation;
    private int selected = -1, navigation;
    private bool armed;
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
        NativeMethods.GetCursorPos(out var pointer);
        bool newSession = session.RequestMode(Environment.TickCount64, requestedMode, pointer.X, pointer.Y);
        generation = session.Generation;
        paging = new();
        selected = -1;
        navigation = 0;
        armed = false;
        if (newSession)
        {
            new WindowInteropHelper(this).EnsureHandle();
            Place(NativeMethods.MonitorScale(pointer));
            Show(); // ShowActivated=false; no Focus, Activate or mouse capture.
            Place(Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0);
        }
        // A mode change keeps native placement and OLE payload ownership; only selection is rearmed.
        var initial = Surface.PointFromScreen(new Point(pointer.X, pointer.Y));
        session.ResetPointer(initial.X, initial.Y);
        operations = catalog.GetOperations(paths, mode);
        ModeLabel.Text = mode.ToString().ToUpperInvariant();
        UpdatePayloadLabel();
        RenderSectors();
        timer.Start();
    }

    private void UpdatePayloadLabel()
    {
        Badge.Text = paths.Count == 0 ? "Drag a file here" : operations.Count == 0 ? "No supported operations" :
            paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} files";
        Hint.Text = paths.Count > 0 && operations.Count == 0 ? "Release to cancel" : "Hold · glide · release";
    }

    private void Place(double scale)
    {
        var anchor = new NativeMethods.Point { X = (int)session.Anchor.X, Y = (int)session.Anchor.Y };
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

    /// <summary>Called by the mouse observer: capture identity now, then schedule only unconfirmed cleanup.</summary>
    public void ObserveGlobalRelease()
    {
        long releasedGeneration = generation;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (session.GlobalRelease(releasedGeneration)) Dismiss();
        }));
    }

    public void Dismiss()
    {
        session.Cancel(generation);
        timer.Stop();
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
            // Any validated physical FileDrop establishes OLE ownership, even in a mode with no targets.
            session.ConfirmPayload(payload);
            operations = catalog.GetOperations(paths, mode);
            paging = new();
            UpdatePayloadLabel();
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
            if (index >= 0 && session.TryCompleteDrop(generation))
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
            var sector = new ShapePath { Data = geometry, StrokeThickness = i == selected ? 3 : 1 };
            sector.SetResourceReference(ShapePath.FillProperty, i == selected ? "Accent" : "PanelBackground");
            sector.SetResourceReference(ShapePath.StrokeProperty, "PrimaryText");
            Sectors.Children.Add(sector);
            var label = new TextBlock { Text = items[i].Label, Width = 73, FontSize = items[i].Label.Length > 9 ? 10 : 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource(i == selected ? "AccentText" : "PrimaryText") };
            label.SetResourceReference(TextBlock.ForegroundProperty, i == selected ? "AccentText" : "PrimaryText");
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

}
