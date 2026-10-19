namespace Citrus.Core;

/// <summary>Tracks physical left/right keys from normal and SYSKEY events, never asynchronous key state.</summary>
public sealed class ModifierObserver(GestureState state)
{
    private readonly HashSet<uint> pressed = [];
    public OperationMode? Observe(uint virtualKey, bool down, long timestamp)
    {
        if (Mode(virtualKey) is not { } mode) return null;
        if (down)
        {
            if (!pressed.Add(virtualKey)) return null;
            // The opposite physical key is a new intentional press of this mode, even while its sibling is held.
            state.KeyUp(mode);
            return state.KeyDown(mode, timestamp);
        }
        pressed.Remove(virtualKey);
        if (!pressed.Any(key => Mode(key) == mode)) state.KeyUp(mode);
        return null;
    }
    public OperationMode? ObserveMessage(int message, uint virtualKey, long timestamp) => message switch
    {
        0x100 or 0x104 => Observe(virtualKey, true, timestamp),
        0x101 or 0x105 => Observe(virtualKey, false, timestamp),
        _ => null
    };
    public void Reset()
    {
        pressed.Clear();
        foreach (var mode in Enum.GetValues<ModeKey>()) state.KeyUp(mode);
    }
    private static ModeKey? Mode(uint key) => key switch
    {
        0x10 or 0xA0 or 0xA1 => ModeKey.Shift,
        0x12 or 0xA4 or 0xA5 => ModeKey.Alt,
        0x11 or 0xA2 or 0xA3 => ModeKey.Control,
        _ => null
    };
}
