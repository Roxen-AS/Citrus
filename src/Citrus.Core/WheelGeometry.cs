namespace Citrus.Core;

/// <summary>Physical screen pixels, including negative virtual-desktop coordinates.</summary>
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public static class WheelGeometry
{
    public const int MaxTargetsPerPage = 9;
    public const double Width = 368, Height = 454;
    public const double CenterX = 184, CenterY = 184, InnerRadius = 65, OuterRadius = 166;
    private static readonly IReadOnlyList<WheelPoint>[][] petals = Enumerable.Range(1, MaxTargetsPerPage)
        .Select(count => Enumerable.Range(0, count).Select(index => BuildPetal(index, count)).ToArray()).ToArray();

    public static ScreenRect Place(double x, double y, ScreenRect work, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        double fit = Math.Min(scale, Math.Min(work.Width / Width, work.Height / Height));
        // Re-multiplication may exceed the limiting work dimension by one ULP.
        double width = Math.Min(work.Width, Width * fit), height = Math.Min(work.Height, Height * fit);
        return new(Math.Clamp(x - CenterX * fit, work.Left, work.Right - width),
            Math.Clamp(y - CenterY * fit, work.Top, work.Bottom - height), width, height);
    }

    // These immutable flattened outlines are used verbatim by both rendering and hit testing.
    // Two-degree arc samples and eight samples per quadratic keep rounding error below 0.03 DIP.
    public static IReadOnlyList<WheelPoint> Petal(int index, int count) => petals[Math.Clamp(count, 1, MaxTargetsPerPage) - 1][index];
    public static WheelPoint At(double radius, double degrees) =>
        new(CenterX + radius * Math.Sin(degrees * Math.PI / 180), CenterY - radius * Math.Cos(degrees * Math.PI / 180));

    public static int HitTest(double x, double y, int count)
    {
        for (int i = 0; i < Math.Clamp(count, 0, MaxTargetsPerPage); i++)
        {
            var points = Petal(i, count);
            bool inside = false;
            for (int j = 0, k = points.Count - 1; j < points.Count; k = j++)
            {
                var a = points[j]; var b = points[k];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            if (inside) return i;
        }
        return -1;
    }

    private static IReadOnlyList<WheelPoint> BuildPetal(int index, int count)
    {
        double start = (index - .5) * 360 / count + 2, end = (index + .5) * 360 / count - 2;
        const double corner = 20;
        double outerInset = corner / OuterRadius * 180 / Math.PI, innerInset = corner / InnerRadius * 180 / Math.PI;
        var points = new List<WheelPoint> { At(InnerRadius + corner, start), At(OuterRadius - corner, start) };
        void Curve(WheelPoint control, WheelPoint to)
        {
            var from = points[^1];
            for (int i = 1; i <= 8; i++)
            {
                double t = i / 8d, u = 1 - t;
                points.Add(new(u*u*from.X + 2*u*t*control.X + t*t*to.X, u*u*from.Y + 2*u*t*control.Y + t*t*to.Y));
            }
        }
        void Arc(double radius, double from, double to)
        {
            int steps = (int)Math.Ceiling(Math.Abs(to-from)/2);
            for (int i = 1; i <= steps; i++) points.Add(At(radius, from + (to-from)*i/steps));
        }
        Curve(At(OuterRadius,start),At(OuterRadius,start+outerInset));
        Arc(OuterRadius,start+outerInset,end-outerInset);
        Curve(At(OuterRadius,end),At(OuterRadius-corner,end));
        points.Add(At(InnerRadius+corner,end));
        Curve(At(InnerRadius,end),At(InnerRadius,end-innerInset));
        Arc(InnerRadius,end-innerInset,start+innerInset);
        Curve(At(InnerRadius,start),At(InnerRadius+corner,start));
        return points.AsReadOnly();
    }
}

public readonly record struct WheelPoint(double X, double Y);

public sealed class WheelSession
{
    private long generation, deadline;
    private double originX, originY;
    private bool confirmed, armed, active;
    public long Generation => generation;
    public bool IsConfirmed => active && confirmed;
    public OperationMode Mode { get; private set; }
    public (double X, double Y) Anchor { get; private set; }
    public IReadOnlyList<string> Paths { get; private set; } = [];
    public bool RequestMode(long now, OperationMode mode, double screenX, double screenY)
    {
        bool newSession = !active;
        if (newSession)
        {
            Begin(now, 0, 0);
            Anchor = (screenX, screenY);
        }
        Mode = mode;
        armed = false;
        return newSession;
    }
    public void ResetPointer(double x, double y)
    {
        originX = x;
        originY = y;
        armed = false;
    }
    public long Begin(long now, double x, double y)
    {
        deadline = now + 750;
        originX = x;
        originY = y;
        confirmed = armed = false;
        active = true;
        Paths = [];
        return ++generation;
    }
    public bool ShouldExpire(long expectedGeneration, long now) => active && expectedGeneration == generation && !confirmed && now >= deadline;
    public void ConfirmPayload(IReadOnlyList<string>? paths = null)
    {
        if (!active || confirmed) return;
        confirmed = true;
        if (paths is not null) Paths = Array.AsReadOnly(paths.ToArray());
    }
    // Once OLE has confirmed the payload, only Drop, DragLeave or explicit cancellation owns teardown.
    public bool GlobalRelease(long expectedGeneration) => !confirmed && Cancel(expectedGeneration);
    public bool TryCompleteDrop(long expectedGeneration) => confirmed && Cancel(expectedGeneration);
    public bool Cancel(long expectedGeneration)
    {
        if (!active || expectedGeneration != generation) return false;
        active = false;
        confirmed = false;
        Paths = [];
        return true;
    }
    public bool ObservePointer(double x, double y)
    {
        armed |= Math.Pow(x - originX, 2) + Math.Pow(y - originY, 2) >= 64;
        return armed;
    }
}

public sealed class WheelPaging
{
    private int direction;
    private int wheelRemainder;
    private long hoverStarted;
    public int Page { get; private set; }
    public int Scroll(int delta, int pageCount)
    {
        long total = (long)wheelRemainder + delta;
        int steps = (int)(total / 120);
        wheelRemainder = (int)(total % 120);
        if (steps == 0) return Page;
        direction = 0; // A wheel step starts a fresh arrow dwell.
        Page = (int)Math.Clamp((long)Page - steps, 0, Math.Max(0, pageCount - 1));
        return Page;
    }
    public int UpdateHover(int nextDirection, long now, int count)
    {
        if (nextDirection != direction) { direction = nextDirection; hoverStarted = now; }
        if (direction != 0 && now - hoverStarted >= 400)
        {
            Page = Math.Clamp(Page + Math.Sign(direction), 0, Math.Max(0, (count - 1) / WheelGeometry.MaxTargetsPerPage));
            hoverStarted = now;
        }
        return Page;
    }
}
