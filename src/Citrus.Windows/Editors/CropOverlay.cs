using System.Windows.Input;
using System.Windows.Media;
using Citrus.Core.Editing;

namespace Citrus.Windows.Editors;

/// <summary>Direct handles operate in oriented G pixels; letterboxing never enters the recipe.</summary>
public sealed class CropOverlay : FrameworkElement
{
    public SizeI Plane { get; set; }
    public RectD Crop { get; set; }
    public double? AspectRatio { get; set; }
    public bool IsDragging { get; private set; }
    public bool VideoStyle { get; set; }
    public event Action<RectD, bool>? Changed;
    private RectD start;
    private Point origin;
    private int handle;
    public CropOverlay() { Focusable = true; Cursor = Cursors.SizeAll; }
    private Rect Frame
    {
        get
        {
            if (Plane.Width <= 0 || Plane.Height <= 0) return Rect.Empty;
            double scale = Math.Min(ActualWidth / Plane.Width, ActualHeight / Plane.Height);
            return new((ActualWidth - Plane.Width * scale) / 2, (ActualHeight - Plane.Height * scale) / 2, Plane.Width * scale, Plane.Height * scale);
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var f = Frame; if (f.IsEmpty || f.Width == 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double scale = f.Width / Plane.Width;
        var r = new Rect(f.X + Crop.X * scale, f.Y + Crop.Y * scale, Crop.Width * scale, Crop.Height * scale);
        var shade = new SolidColorBrush(Color.FromArgb(140,0,0,0)); shade.Freeze();
        dc.DrawGeometry(shade, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(f), new RectangleGeometry(r)));
        var accent = VideoStyle?Brushes.White:new SolidColorBrush(Color.FromRgb(255,85,0));
        dc.DrawRectangle(null, new Pen(accent, 2), r);
        if (!VideoStyle)
            foreach (var p in Handles(r)) dc.DrawRoundedRectangle(accent, null, new(p.X - 5, p.Y - 5, 10, 10),2,2);
        else
        {
            // The video reference uses an inset white resize affordance, leaving the source unobscured.
            var grip = VideoHandle(r, 2);
            dc.DrawRoundedRectangle(Brushes.White, null, grip, 5, 5);
            var pen = new Pen(Brushes.Black, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            double x = grip.X + 6, y = grip.Y + 6;
            dc.DrawLine(pen, new(x, y), new(x + 10, y + 10));
            dc.DrawLine(pen, new(x, y), new(x + 4, y)); dc.DrawLine(pen, new(x, y), new(x, y + 4));
            dc.DrawLine(pen, new(x + 10, y + 10), new(x + 6, y + 10)); dc.DrawLine(pen, new(x + 10, y + 10), new(x + 10, y + 6));
        }
    }
    private static Point[] Corners(Rect r) => [r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft];
    private static Point[] Handles(Rect r)=>[..Corners(r),new(r.X+r.Width/2,r.Top),new(r.Right,r.Y+r.Height/2),new(r.X+r.Width/2,r.Bottom),new(r.Left,r.Y+r.Height/2)];
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var frame = Frame; if (frame.IsEmpty || frame.Width == 0) return;
        Focus(); origin = e.GetPosition(this); start = Crop;
        double scale = frame.Width / Plane.Width;
        var rect = new Rect(frame.X + Crop.X * scale, frame.Y + Crop.Y * scale, Crop.Width * scale, Crop.Height * scale);
        handle = HitHandle(rect, origin, VideoStyle);
        if (handle < 0 && !rect.Contains(origin)) return;
        IsDragging = CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsDragging) return;
        var delta = e.GetPosition(this) - origin; double scale = Frame.Width / Plane.Width;
        Crop = Drag(start, Plane, handle, delta.X / scale, delta.Y / scale, AspectRatio);
        InvalidateVisual(); Changed?.Invoke(Crop, false); e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsDragging) return;
        IsDragging = false; ReleaseMouseCapture(); Changed?.Invoke(Crop, true); e.Handled = true;
    }
    public void CancelDrag() { if (!IsDragging) return; Crop = start; IsDragging = false; ReleaseMouseCapture(); InvalidateVisual(); }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (IsDragging) { IsDragging = false; Crop = start; InvalidateVisual(); Changed?.Invoke(Crop, true); }
        base.OnLostMouseCapture(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var delta = e.Key switch { Key.Left => new Vector(-step,0), Key.Right => new Vector(step,0), Key.Up => new Vector(0,-step), Key.Down => new Vector(0,step), _ => default };
        if (delta == default) { base.OnKeyDown(e); return; }
        Crop = Drag(Crop, Plane, -1, delta.X, delta.Y, null); InvalidateVisual(); Changed?.Invoke(Crop, true); e.Handled = true;
    }
    internal static RectD Drag(RectD start, SizeI plane, int corner, double dx, double dy, double? ratio) => Image.CropGestureGeometry.Drag(start, plane, corner, dx, dy, ratio);
    internal static int HitHandle(Rect rect, Point point, bool video) => video
        ? Enumerable.Range(0, 4).FirstOrDefault(index => VideoHandle(rect, index).Contains(point), -1)
        : Array.FindIndex(Handles(rect), p => (p - point).Length <= 14);
    private static Rect VideoHandle(Rect rect, int corner)
    {
        double size = Math.Min(22, Math.Min(rect.Width / 2, rect.Height / 2));
        return new(corner is 0 or 3 ? rect.Left : rect.Right - size, corner is 0 or 1 ? rect.Top : rect.Bottom - size, size, size);
    }
}
