namespace Citrus.Core.Editing;

/// <summary>At most 100 immutable states, including the current state. Consecutive gesture keys coalesce.</summary>
public sealed class EditorHistory
{
    private readonly EditorDocument initial;
    private readonly List<EditorDocument> states;
    private int index;
    private long revision;
    private string? lastKey;
    public EditorHistory(EditorDocument initial)
    {
        this.initial = initial;
        states = [initial];
        Current = initial;
        revision = initial.Revision;
    }
    public EditorDocument Current { get; private set; }
    public void Commit(EditorDocument next, string coalescingKey)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentException.ThrowIfNullOrWhiteSpace(coalescingKey);
        if (index + 1 < states.Count) states.RemoveRange(index + 1, states.Count - index - 1);
        Current = next with { Revision = checked(++revision) };
        if (lastKey == coalescingKey && index > 0) states[index] = Current;
        else { states.Add(Current); index++; }
        if (states.Count > 100) { states.RemoveAt(0); index--; }
        lastKey = coalescingKey;
    }
    public bool Undo()
    {
        if (index == 0) return false;
        Current = states[--index] with { Revision = checked(++revision) };
        lastKey = null;
        return true;
    }
    public bool Redo()
    {
        if (index + 1 == states.Count) return false;
        Current = states[++index] with { Revision = checked(++revision) };
        lastKey = null;
        return true;
    }
    public void Reset() { lastKey = null; Commit(initial, "reset"); lastKey = null; }
}
