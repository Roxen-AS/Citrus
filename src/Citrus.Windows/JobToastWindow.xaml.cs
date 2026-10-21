using System.Windows.Interop;
using System.Windows.Threading;

namespace Citrus.Windows;

/// <summary>Owned resident notification. Showing/progress never activate; only an explicit View click opens Home.</summary>
public partial class JobToastWindow : Window, IDisposable
{
    private readonly Action viewJobs;
    private readonly Func<IReadOnlyList<string>, Task> reveal;
    private readonly Func<bool> canShow;
    private readonly DispatcherTimer timer;
    private IReadOnlyList<string> outputs = [];
    private HwndSource? source;
    private bool disposed, positioning;
    private NativeMethods.Point? anchor;
    private long expiresAt = long.MaxValue;
    public JobToastWindow(Action viewJobs, Func<IReadOnlyList<string>, Task> reveal, Func<bool> canShow)
    {
        this.viewJobs = viewJobs; this.reveal = reveal; this.canShow = canShow;
        InitializeComponent();
        Presentation.WindowChromeSetup.Apply(this);
        SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowLongPtr(handle, -20, NativeMethods.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x00000080);
            source = HwndSource.FromHwnd(handle); source?.AddHook(NoActivation);
        };
        timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += Tick;
        SizeChanged += (_, _) => ReanchorVisibleToast();
        Closed += (_, _) => Dispose();
        (Application.Current as App)?.AttachPresentation(this, Chrome);
    }
    public void Update(string message, bool pending, IReadOnlyList<string>? completedOutputs = null, string? operation = null, bool autoDismiss = false, double? fraction = null)
    {
        if (disposed) return;
        OperationTitle.Text = operation ?? "Citrus";
        Message.Text = message;
        Progress.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        Progress.IsIndeterminate = pending && (fraction is not double progress || !double.IsFinite(progress));
        Progress.Value = fraction is double value && double.IsFinite(value) ? Math.Clamp(value * 100, 0, 100) : 0;
        Progress.Margin = new Thickness(0, 0, 0, pending ? 0 : 12);
        ActionRow.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
        outputs = completedOutputs?.ToArray() ?? [];
        expiresAt = autoDismiss && !pending && outputs.Count > 0 ? Environment.TickCount64 + 1800 : long.MaxValue;
        RevealButton.Visibility = outputs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        timer.Start(); Tick(this, EventArgs.Empty);
        ReanchorVisibleToast();
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (disposed) return;
        if (Environment.TickCount64 >= expiresAt) { Close(); return; }
        if (!canShow()) { Hide(); return; }
        if (!IsVisible)
        {
            // Physical monitor work coordinates are converted to DIP using that monitor's DPI.
            if (anchor is null) { NativeMethods.GetCursorPos(out var initial); anchor = initial; }
            var pointer = anchor.Value;
            var work = NativeMethods.WorkArea(pointer);
            double scale = NativeMethods.MonitorScale(pointer);
            Left = pointer.X / scale - Width / 2; Top = pointer.Y / scale - 180;
            Show();
            PositionNearAction(work, scale);
        }
    }
    internal void RefreshVisibility() => Tick(this, EventArgs.Empty);
    private void ReanchorVisibleToast()
    {
        if (disposed || !IsVisible || positioning) return;
        // Stay on the toast's current monitor even if the pointer has since moved.
        var origin = PointToScreen(new Point(0, 0));
        var point = new NativeMethods.Point { X = (int)origin.X + 1, Y = (int)origin.Y + 1 };
        PositionNearAction(NativeMethods.WorkArea(point), NativeMethods.MonitorScale(point));
    }
    private void PositionNearAction(Citrus.Core.ScreenRect work, double scale)
    {
        if (disposed || positioning) return;
        positioning = true;
        try
        {
            // Layout can raise SizeChanged synchronously; the guard prevents recursive placement.
            UpdateLayout();
            var pointer = anchor ?? new NativeMethods.Point { X = (int)work.Right, Y = (int)work.Bottom };
            double width = ActualWidth * scale, height = ActualHeight * scale;
            double left = Math.Clamp(pointer.X - width / 2, work.Left, Math.Max(work.Left, work.Right - width));
            double top = Math.Clamp(pointer.Y - height - 16 * scale, work.Top, Math.Max(work.Top, work.Bottom - height));
            NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, 0,
                (int)left, (int)top,
                (int)(ActualWidth * scale), (int)(ActualHeight * scale), 0x14); // NOACTIVATE | NOZORDER
        }
        finally { positioning = false; }
    }
    private static nint NoActivation(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // MA_NOACTIVATE; child buttons still receive clicks.
        return 0;
    }
    private void Dismiss(object sender, RoutedEventArgs e) => Close();
    private void ViewJobs(object sender, RoutedEventArgs e) { viewJobs(); Close(); }
    private async void Reveal(object sender, RoutedEventArgs e) { await reveal(outputs); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); timer.Tick -= Tick;
        source?.RemoveHook(NoActivation); source = null;
    }
}
