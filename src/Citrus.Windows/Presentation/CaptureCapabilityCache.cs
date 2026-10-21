namespace Citrus.Windows.Presentation;

/// <summary>Preparation and activation share successful prerequisites, never capture resources.</summary>
internal sealed class CaptureCapabilityCache(Func<bool> supported, Func<bool> access)
{
    private readonly object gate = new();
    private bool available;

    internal bool Resolve()
    {
        lock (gate)
        {
            if (available) return true;
            // Negative results and exceptions leave foreground activation able to
            // retry prerequisites that were unavailable during optional preparation.
            available = supported() && access();
            return available;
        }
    }
}
