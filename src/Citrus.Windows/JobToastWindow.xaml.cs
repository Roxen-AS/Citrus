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
    public JobToastWindow(Action viewJobs, Func<IReadOnlyList<string>, Task> reveal, Func<bool> canShow)
    {
        this.viewJobs = viewJobs; this.reveal = reveal; this.canShow = canShow;
        InitializeComponent();
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
    }
    public void Update(string message, bool pending, IReadOnlyList<string>? completedOutputs = null)
    {
        if (disposed) return;
        Message.Text = message;
        Progress.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        outputs = completedOutputs?.ToArray() ?? [];
        RevealButton.Visibility = outputs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        timer.Start(); Tick(this, EventArgs.Empty);
        ReanchorVisibleToast();
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (disposed) return;
        if (!canShow()) { Hide(); return; }
        if (!IsVisible)
        {
            // Physical monitor work coordinates are converted to DIP using that monitor's DPI.
            NativeMethods.GetCursorPos(out var pointer);
            var work = NativeMethods.WorkArea(pointer);
            double scale = NativeMethods.MonitorScale(pointer);
            Left = work.Right / scale - Width - 16; Top = work.Bottom / scale - 220;
            Show();
            PositionAtEdge(work, scale);
        }
    }
    private void ReanchorVisibleToast()
    {
        if (disposed || !IsVisible || positioning) return;
        // Stay on the toast's current monitor even if the pointer has since moved.
        var origin = PointToScreen(new Point(0, 0));
        var point = new NativeMethods.Point { X = (int)origin.X + 1, Y = (int)origin.Y + 1 };
        PositionAtEdge(NativeMethods.WorkArea(point), NativeMethods.MonitorScale(point));
    }
    private void PositionAtEdge(Citrus.Core.ScreenRect work, double scale)
    {
        if (disposed || positioning) return;
        positioning = true;
        try
        {
            // Layout can raise SizeChanged synchronously; the guard prevents recursive placement.
            UpdateLayout();
            NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, 0,
                (int)(work.Right - ActualWidth * scale - 16 * scale), (int)(work.Bottom - ActualHeight * scale - 16 * scale),
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
