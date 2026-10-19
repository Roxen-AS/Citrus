namespace Citrus.Core;

/// <summary>Physical screen pixels, including negative virtual-desktop coordinates.</summary>
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public static class WheelGeometry
{
    public const double Width = 336, Height = 382;
    public const double CenterX = 168, CenterY = 162, InnerRadius = 70, OuterRadius = 146;

    public static ScreenRect Place(double x, double y, ScreenRect work, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        double width = Math.Min(Width * scale, work.Width), height = Math.Min(Height * scale, work.Height);
        return new(Math.Clamp(x - CenterX * scale, work.Left, work.Right - width),
            Math.Clamp(y - CenterY * scale, work.Top, work.Bottom - height), width, height);
    }

    /// <summary>WPF local DIPs. Fixed eight-sector ring; absent sectors and gaps are neutral.</summary>
    public static int HitTest(double x, double y, int count)
    {
        double dx = x - CenterX, dy = y - CenterY, radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius < InnerRadius || radius > OuterRadius || count <= 0) return -1;
        double angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 382.5) % 360;
        int sector = (int)(angle / 45);
        if (angle % 45 < 1.5 || angle % 45 > 43.5) return -1;
        return sector < Math.Min(8, count) ? sector : -1;
    }
}

public sealed class WheelSession
{
    private long generation, deadline;
    private double originX, originY;
    private bool confirmed, armed, active;
    public long Generation => generation;
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
        if (!active) return;
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
    private long hoverStarted;
    public int Page { get; private set; }
    public int UpdateHover(int nextDirection, long now, int count)
    {
        if (nextDirection != direction) { direction = nextDirection; hoverStarted = now; }
        if (direction != 0 && now - hoverStarted >= 400)
        {
            Page = Math.Clamp(Page + Math.Sign(direction), 0, Math.Max(0, (count - 1) / 8));
            hoverStarted = now;
        }
        return Page;
    }
}
