namespace Citrus.Windows.Presentation;

internal readonly record struct GlassFrameState(int AccentRegion = 0, double PointerXDip = double.NaN,
    double PointerYDip = double.NaN, double Illumination = 0, double Materialize = 1, bool Dark = false,
    bool NavigationVisible = true)
{
    public GlassFrameState() : this(0, double.NaN, double.NaN, 0, 1, false, true) { }
}

/// <summary>A session owns reusable rendering resources, never caller destination buffers.</summary>
internal interface ILiveGlassSession : IDisposable
{
    GlassScene Scene { get; }
    bool TryRenderFrame(GlassFrameState state, byte[] destination);
}

/// <summary>A session can change desktop capture coordinates without rebuilding its scene resources.</summary>
internal interface IRelocatableGlassSession
{
    bool TryMoveTo(int screenLeft, int screenTop);
}
