using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace Citrus.Windows.Editors.Media;

/// <summary>A source-time selection, independent of output duration or playback preparation.</summary>
public sealed class VideoTrimSelectionView : FrameworkElement
{
    public double Duration { get; set; } = 1;
    public double SelectionStart { get; set; }
    public double SelectionEnd { get; set; } = 1;
    private double position;
    public double Position { get => position; set { if (position == value) return; position = value; InvalidateVisual(); } }
    public bool IsDragging { get; private set; }
    public event Action<double, double, bool>? SelectionChanged;
    public event Action<double>? SeekRequested;
    public event Action? DragStateChanged;
    private bool movingStart;
    private bool seeking;
    private double savedStart, savedEnd, savedPosition;
    public VideoTrimSelectionView()
    {
        Height = 46; MinHeight = 46; Focusable = true; Cursor = Cursors.Hand;
        AutomationProperties.SetName(this, "Video trim selection. Drag the orange handles. Click the track to seek. Left and right adjust the start; Shift adjusts the end; Control seeks the playhead.");
    }
    private double X(double value) => 8 + Math.Clamp(value / Math.Max(.001, Duration), 0, 1) * Math.Max(1, ActualWidth - 16);
    private double At(Point point) => Math.Clamp((point.X - 8) / Math.Max(1, ActualWidth - 16), 0, 1) * Duration;
    public void SetHandle(bool start, double seconds, bool commit)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(Duration) || Duration <= 0) return;
        double minimum = Math.Min(.001, Duration);
        if (start) SelectionStart = Math.Clamp(seconds, 0, Math.Max(0, SelectionEnd - minimum));
        else SelectionEnd = Math.Clamp(seconds, Math.Min(Duration, SelectionStart + minimum), Duration);
        SelectionChanged?.Invoke(SelectionStart, SelectionEnd, commit); InvalidateVisual();
    }
    public void CancelDrag()
    {
        if (!IsDragging) return;
        IsDragging = false; SelectionStart = savedStart; SelectionEnd = savedEnd;
        ReleaseMouseCapture();
        DragStateChanged?.Invoke();
        if (seeking) { Position = savedPosition; SeekRequested?.Invoke(Position); }
        else SelectionChanged?.Invoke(SelectionStart, SelectionEnd, false);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Brush accent = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromRgb(255, 85, 0));
        Brush track = SystemParameters.HighContrast ? SystemColors.ControlBrush : new SolidColorBrush(Color.FromRgb(232, 235, 236));
        Brush text = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.DimGray;
        dc.DrawRoundedRectangle(track, null, new(8, 4, Math.Max(1, ActualWidth - 16), 16), 4, 4);
        double left = X(SelectionStart), right = X(SelectionEnd);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(65, 255, 85, 0)), new Pen(accent, 1.2), new(left, 4, Math.Max(1, right - left), 16), 4, 4);
        foreach (double edge in new[] { left, right })
        {
            dc.DrawRoundedRectangle(accent, null, new(edge - 5, 0, 10, 24), 3, 3);
            dc.DrawLine(new Pen(Brushes.White, 1.4), new(edge, 7), new(edge, 17));
        }
        dc.DrawLine(new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.DimGray, 1.5), new(X(Position), 0), new(X(Position), 24));
        Draw(MediaRangeView.Timecode(SelectionStart), 0);
        string end = MediaRangeView.Timecode(SelectionEnd);
        Draw(end, Math.Max(0, ActualWidth - 76));
        void Draw(string value, double x) => dc.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, text, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, 27));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); BeginPointer(e.GetPosition(this)); e.Handled = true;
    }
    internal void BeginPointer(Point point)
    {
        Focus();
        double a = Math.Abs(point.X - X(SelectionStart)), b = Math.Abs(point.X - X(SelectionEnd));
        seeking = Math.Min(a, b) > 16; movingStart = a <= b; savedStart = SelectionStart; savedEnd = SelectionEnd; savedPosition = Position;
        IsDragging = CaptureMouse();
        DragStateChanged?.Invoke();
        if (seeking) SeekTo(point);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); MovePointer(e.GetPosition(this)); }
    internal void MovePointer(Point point)
    {
        if (!IsDragging) return;
        if (seeking) SeekTo(point); else SetHandle(movingStart, At(point), false);
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsDragging) return;
        EndPointer(e.GetPosition(this)); e.Handled = true;
    }
    internal void EndPointer(Point point)
    {
        if (!IsDragging) return;
        MovePointer(point); IsDragging = false; ReleaseMouseCapture();
        DragStateChanged?.Invoke();
        if (!seeking) SelectionChanged?.Invoke(SelectionStart, SelectionEnd, true);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    { base.OnLostMouseCapture(e); if (IsDragging) { IsDragging = false; DragStateChanged?.Invoke(); if (!seeking) SelectionChanged?.Invoke(SelectionStart, SelectionEnd, true); } }
    private void SeekTo(Point point) { Position = At(point); SeekRequested?.Invoke(Position); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Home or Key.End) { SeekRequested?.Invoke(e.Key == Key.Home ? 0 : Duration); e.Handled = true; return; }
        if (e.Key is not Key.Left and not Key.Right) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SeekRequested?.Invoke(Math.Clamp(Position + (e.Key == Key.Left ? -.1 : .1), 0, Duration)); e.Handled = true; return; }
        bool start = !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        SetHandle(start, (start ? SelectionStart : SelectionEnd) + (e.Key == Key.Left ? -.01 : .01), true); e.Handled = true;
    }
}
