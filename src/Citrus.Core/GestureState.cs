namespace Citrus.Core;

public enum ModeKey { Shift, Alt, Control }

/// <summary>Pure observer: transitions never consume input and releasing a modifier does not unlatch.</summary>
public sealed class GestureState(double thresholdX, double thresholdY)
{
    // A snapshot tells us a key is held, but cannot tell us when it was pressed.
    private readonly Dictionary<ModeKey, long?> held = [];
    private bool buttonDown, dragging;
    private double originX, originY;
    private long lastTimestamp = long.MinValue;
    private ModeKey lastKey;
    /// <summary>Immutable identity captured by queued work; invalidated by each new press or cancellation.</summary>
    public long Epoch { get; private set; }
    public OperationMode? LatchedMode { get; private set; }

    internal void SeedHeld(ModeKey key) => held.TryAdd(key, null);

    public OperationMode? KeyDown(ModeKey key, long timestamp)
    {
        if (!held.TryAdd(key, timestamp)) return null;
        if (!dragging) return null;
        if (timestamp == lastTimestamp && key < lastKey) return null;
        return Latch(key, timestamp);
    }

    public void KeyUp(ModeKey key)
    {
        held.Remove(key);
    }
    public void ButtonDown(double x, double y)
    {
        Epoch++;
        buttonDown = true;
        dragging = false;
        originX = x;
        originY = y;
        LatchedMode = null;
        lastTimestamp = long.MinValue;
    }

    public void ButtonUp()
    {
        Epoch++;
        buttonDown = dragging = false;
        LatchedMode = null;
    }

    public OperationMode? Move(double x, double y, long timestamp = 0)
    {
        if (!buttonDown || dragging || (Math.Abs(x - originX) < thresholdX && Math.Abs(y - originY) < thresholdY)) return null;
        dragging = true;
        if (held.Count == 0) return null;
        var latest = held.OrderByDescending(entry => entry.Value).ThenByDescending(entry => entry.Key).First();
        // Crossing the drag threshold distinguishes a drag from a selection click; key age adds no delay.
        return Latch(latest.Key, latest.Value ?? long.MinValue);
    }

    private OperationMode Latch(ModeKey key, long timestamp)
    {
        lastKey = key;
        lastTimestamp = timestamp;
        var mode = key switch { ModeKey.Control => OperationMode.Actions, ModeKey.Alt => OperationMode.Tools, _ => OperationMode.Convert };
        LatchedMode = mode;
        return mode;
    }
}
