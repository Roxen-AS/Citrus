namespace Citrus.Windows.Presentation;

/// <summary>Only successful frames advance this stamp; failed capture remains eligible to retry.</summary>
internal sealed class StudioGlassFrameInvalidation
{
    private long[] versions = [];
    private GlassFrameState state;
    private long placement, exclusion;
    private bool rendered;

    internal bool NeedsRender(IReadOnlyList<long> nextVersions, GlassFrameState nextState, long nextPlacement, long nextExclusion)
    {
        return state != nextState || NeedsSource(nextVersions, nextPlacement, nextExclusion);
    }

    internal bool NeedsSource(IReadOnlyList<long> nextVersions, long nextPlacement, long nextExclusion)
    {
        if (!rendered || placement != nextPlacement || exclusion != nextExclusion || versions.Length != nextVersions.Count) return true;
        for (int index = 0; index < versions.Length; index++)
            if (nextVersions[index] < 0 || versions[index] != nextVersions[index]) return true;
        return false;
    }

    internal void Commit(IReadOnlyList<long> nextVersions, GlassFrameState nextState, long nextPlacement, long nextExclusion)
    {
        if (versions.Length != nextVersions.Count) versions = new long[nextVersions.Count];
        for (int index = 0; index < versions.Length; index++) versions[index] = nextVersions[index];
        state = nextState; placement = nextPlacement; exclusion = nextExclusion; rendered = true;
    }
}
