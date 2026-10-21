namespace Citrus.Windows.Presentation;

/// <summary>Submission and completion never wait for GPU progress. Retirement owns outstanding resources.</summary>
internal interface IStagedLiveGlassSession : ILiveGlassSession
{
    bool HasPendingFrames { get; }
    bool TrySubmitFrame(GlassFrameState state, long stamp);
    bool TryCompleteFrame(byte[] destination, out long stamp);
    Task RetireAsync();
}
