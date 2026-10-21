using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Citrus.Windows.Presentation;

internal readonly record struct GlassMonitor(nint Handle, int Left, int Top, int Width, int Height);
internal readonly record struct GlassCaptureTile(GlassMonitor Monitor, int SourceLeft, int SourceTop,
    int DestinationLeft, int DestinationTop, int Width, int Height);

internal static class StudioGlassTiling
{
    internal static IReadOnlyList<GlassCaptureTile> Plan(int left, int top, int width, int height,
        IReadOnlyList<GlassMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        long right = (long)left + width, bottom = (long)top + height;
        var tiles = new List<GlassCaptureTile>();
        var seen = new HashSet<nint>();
        foreach (var monitor in monitors)
        {
            if (monitor.Width <= 0 || monitor.Height <= 0 || !seen.Add(monitor.Handle)) continue;
            long x = Math.Max(left, monitor.Left), y = Math.Max(top, monitor.Top);
            long endX = Math.Min(right, (long)monitor.Left + monitor.Width);
            long endY = Math.Min(bottom, (long)monitor.Top + monitor.Height);
            if (endX <= x || endY <= y) continue;
            tiles.Add(new(monitor, checked((int)(x - monitor.Left)), checked((int)(y - monitor.Top)),
                checked((int)(x - left)), checked((int)(y - top)), checked((int)(endX - x)), checked((int)(endY - y))));
        }
        return tiles;
    }
}

/// <summary>A surface's padded physical-desktop tiles on the shared, serialized D3D device.</summary>
internal sealed class StudioGlassSession : IStagedLiveGlassSession, IRelocatableGlassSession, IGlassSessionTiming
{
    private readonly object gate = new();
    private IReadOnlyList<MonitorLease> captures;
    private readonly Func<IReadOnlyList<GlassMonitor>> getMonitors;
    private readonly Func<int, int, DesktopGlassCapturePool.Lease?> acquireCapture;
    private int screenLeft, screenTop;
    private readonly List<StudioGlassTile> tiles = [];
    private readonly List<long> frameVersions = [];
    private readonly StudioGlassFrameInvalidation invalidation = new();
    private D3D11StudioRenderer? renderer;
    private StudioGlassRuntime.Lease? prepared;
    private GlassSessionTiming startupTiming;
    private long placementRevision;
    private bool disposed;
    private bool retiring, retirementFailed;
    private Task? retirement;
    private long compatibilityStamp;
    private string diagnostic = "Session created; awaiting monitor textures.";
    private static string lastCreationDiagnostic = "No Studio session creation attempted.";
    internal string Diagnostic => Volatile.Read(ref diagnostic);
    internal static string LastCreationDiagnostic => Volatile.Read(ref lastCreationDiagnostic);
    public GlassScene Scene { get; }
    public GlassSessionTiming StartupTiming { get { lock (gate) return startupTiming; } }

    private StudioGlassSession(GlassScene scene, int screenLeft, int screenTop, IReadOnlyList<MonitorLease> captures,
        Func<IReadOnlyList<GlassMonitor>> getMonitors, Func<int, int, DesktopGlassCapturePool.Lease?> acquireCapture)
    {
        Scene = scene; this.screenLeft = screenLeft; this.screenTop = screenTop; this.captures = captures;
        this.getMonitors = getMonitors; this.acquireCapture = acquireCapture;
        startupTiming = new(CaptureInitialized: Stopwatch.GetTimestamp());
    }

    internal static StudioGlassSession? TryCreate(GlassScene scene, int screenLeft, int screenTop)
    {
        var session = TryCreate(scene, screenLeft, screenTop, DesktopGlassCapturePool.Monitors, DesktopGlassCapturePool.TryAcquire);
        if (session is null) return null;
        try
        {
            // WGC is already starting. GPU allocation therefore overlaps its first
            // compositor frame rather than waiting until that frame is available.
            session.prepared = StudioGlassRuntime.Shared.Rent(scene);
            session.renderer = session.prepared.Renderer;
            session.startupTiming = session.startupTiming with
            { RendererReady = Stopwatch.GetTimestamp(), PreparedResources = session.prepared.Prepared };
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal static StudioGlassSession? TryCreate(GlassScene scene, int screenLeft, int screenTop,
        Func<IReadOnlyList<GlassMonitor>> getMonitors, Func<int, int, DesktopGlassCapturePool.Lease?> acquireCapture)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(getMonitors);
        ArgumentNullException.ThrowIfNull(acquireCapture);
        var captures = TryCreatePlan(scene, screenLeft, screenTop, getMonitors, acquireCapture, out string diagnostic);
        Volatile.Write(ref lastCreationDiagnostic, diagnostic);
        return captures is null ? null : new StudioGlassSession(scene, screenLeft, screenTop, captures, getMonitors, acquireCapture);
    }

    public bool TryMoveTo(int screenLeft, int screenTop)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (screenLeft == this.screenLeft && screenTop == this.screenTop) return true;
            // Keep every old lease alive until the replacement is complete, including monitors
            // shared by both plans. Their capture stream and shared D3D device never restart.
            var replacement = TryCreatePlan(Scene, screenLeft, screenTop, getMonitors, acquireCapture, out string result);
            if (replacement is null)
            {
                Volatile.Write(ref diagnostic, $"Relocation unavailable. {result}");
                return false;
            }
            var previous = captures;
            captures = replacement;
            this.screenLeft = screenLeft; this.screenTop = screenTop;
            placementRevision++;
            foreach (var capture in previous) capture.Lease.Dispose();
            Volatile.Write(ref diagnostic, $"Studio session relocated with {captures.Count} monitor tiles.");
            return true;
        }
    }

    private static IReadOnlyList<MonitorLease>? TryCreatePlan(GlassScene scene, int screenLeft, int screenTop,
        Func<IReadOnlyList<GlassMonitor>> getMonitors, Func<int, int, DesktopGlassCapturePool.Lease?> acquireCapture,
        out string diagnostic)
    {
        long paddedLeft = (long)screenLeft - scene.PaddingPixels, paddedTop = (long)screenTop - scene.PaddingPixels;
        if (paddedLeft is < int.MinValue or > int.MaxValue || paddedTop is < int.MinValue or > int.MaxValue)
        { diagnostic = "Padded surface origin is outside physical desktop coordinate bounds."; return null; }
        var acquired = new List<MonitorLease>();
        bool accepted = false;
        try
        {
            var plan = StudioGlassTiling.Plan((int)paddedLeft, (int)paddedTop, scene.CaptureWidth, scene.CaptureHeight,
                getMonitors());
            if (plan.Count == 0)
            { diagnostic = "Padded surface does not intersect a monitor."; return null; }
            foreach (var tile in plan)
            {
                var capture = acquireCapture(
                    checked(tile.Monitor.Left + tile.SourceLeft + tile.Width / 2),
                    checked(tile.Monitor.Top + tile.SourceTop + tile.Height / 2));
                if (capture is null)
                { diagnostic = DesktopGlassSource.LastCreationDiagnostic; return null; }
                acquired.Add(new(tile, capture));
            }
            accepted = true;
            diagnostic = $"Studio session created with {acquired.Count} monitor tiles.";
            return acquired;
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException or OverflowException)
        { diagnostic = $"{error.GetType().Name} 0x{error.HResult:X8}: {error.Message}"; return null; }
        finally
        {
            if (!accepted) foreach (var capture in acquired) capture.Lease.Dispose();
        }
    }

    public bool HasPendingFrames
    {
        get { lock (gate) return renderer is not null && renderer.PendingReadbackCount != 0; }
    }

    // Compatibility for generated/owned fixtures. Production uses separate
    // submission/completion and never waits for GPU progress here.
    public bool TryRenderFrame(GlassFrameState state, byte[] destination)
    {
        if (!TrySubmitFrame(state, Interlocked.Increment(ref compatibilityStamp)))
            return TryCompleteFrame(destination, out _);
        while (!TryCompleteFrame(destination, out _)) Thread.Sleep(1);
        return true;
    }

    public bool TrySubmitFrame(GlassFrameState state, long stamp)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (retiring) return false;
            // Do not dequeue/copy another monitor frame while both output slots
            // are occupied. The source pool retains its latest available frame.
            lock (DesktopGlassCapturePool.GpuGate)
                if (renderer is not null && renderer.FreeReadbackSlots == 0) return false;
            long expectedRevision = StudioCaptureExclusion.ContentRevision;
            nint renderDevice = 0, renderContext = 0;
            try
            {
                // Acquire one monitor at a time; never nest monitor locks. AddRef keeps each GPU
                // snapshot alive across queue refreshes while another surface uses the same source.
                foreach (var capture in captures)
                {
                    bool ready = capture.Lease.TryUseLatestFrame((device, context, texture, width, height, left, top, version) =>
                    {
                        var tile = capture.Tile;
                        if (left != tile.Monitor.Left || top != tile.Monitor.Top ||
                            width != tile.Monitor.Width || height != tile.Monitor.Height)
                            throw new InvalidOperationException("Monitor topology changed; recreate the tiled glass session.");
                        if (renderDevice == 0) { renderDevice = device; renderContext = context; }
                        else if (renderDevice != device || renderContext != context)
                            throw new InvalidOperationException("Monitor tiles must share one D3D device and immediate context.");
                        Marshal.AddRef(texture);
                        tiles.Add(new(texture, width, height, tile.SourceLeft, tile.SourceTop,
                            tile.DestinationLeft, tile.DestinationTop, tile.Width, tile.Height));
                        frameVersions.Add(version);
                    });
                    if (!ready)
                    {
                        Volatile.Write(ref diagnostic, $"Waiting for tile {tiles.Count}/{captures.Count}. {capture.Lease.Diagnostic}");
                        return false;
                    }
                }
                lock (DesktopGlassCapturePool.GpuGate)
                {
                    if (expectedRevision != StudioCaptureExclusion.ContentRevision) return false;
                    if (startupTiming.FirstFrame == 0) startupTiming = startupTiming with { FirstFrame = Stopwatch.GetTimestamp() };
                    // A backing has no selection/pointer optics. Its white foreground is
                    // retained WPF geometry and must not cause GPU work on every hover.
                    var visualState = Scene.IsWheelBacking ? state with
                    { AccentRegion = 0, PointerXDip = double.NaN, PointerYDip = double.NaN, Illumination = 0, Dark = false } : state;
                    if (!invalidation.NeedsRender(frameVersions, visualState, placementRevision, expectedRevision)) return false;
                    if (renderer is null)
                    {
                        renderer = new D3D11StudioRenderer(renderDevice, renderContext, Scene);
                        startupTiming = startupTiming with { RendererReady = Stopwatch.GetTimestamp() };
                    }
                    if (prepared is not null && (prepared.Device != renderDevice || prepared.Context != renderContext))
                        throw new InvalidOperationException("Prepared backing must share the monitor capture device.");
                    bool submitted = invalidation.NeedsSource(frameVersions, placementRevision, expectedRevision)
                        ? renderer.TrySubmitTiles(tiles, visualState, stamp)
                        : renderer.TrySubmitCached(visualState, stamp);
                    if (!submitted) return false;
                    if (expectedRevision != StudioCaptureExclusion.ContentRevision) return false;
                    invalidation.Commit(frameVersions, visualState, placementRevision, expectedRevision);
                }
                Volatile.Write(ref diagnostic, $"Rendered {captures.Count} monitor tiles.");
                return expectedRevision == StudioCaptureExclusion.ContentRevision;
            }
            finally
            {
                foreach (var tile in tiles) Marshal.Release(tile.Texture);
                tiles.Clear();
                frameVersions.Clear();
            }
        }
    }

    public bool TryCompleteFrame(byte[] destination, out long stamp)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Length != checked(Scene.PixelWidth * Scene.PixelHeight * 4))
            throw new ArgumentException("Expected one tightly packed premultiplied BGRA surface.", nameof(destination));
        lock (gate)
        {
            stamp = 0;
            if (disposed || retiring || renderer is null) return false;
            lock (DesktopGlassCapturePool.GpuGate)
            {
                if (!renderer.TryComplete(destination, out stamp)) return false;
                startupTiming = startupTiming with { RenderCompleted = Stopwatch.GetTimestamp(),
                    RenderMilliseconds = renderer.LastRenderMilliseconds, ReadbackMilliseconds = renderer.LastReadbackMilliseconds };
                return true;
            }
        }
    }

    public Task RetireAsync()
    {
        lock (gate)
        {
            retiring = true;
            return retirement ??= DrainReadbacksAsync();
        }
    }

    private async Task DrainReadbacksAsync()
    {
        using var completionDelay = new GlassRenderDelay();
        try
        {
            var deadline = Stopwatch.StartNew();
            lock (gate)
            {
                if (renderer is null) return;
                renderer.InvalidateReadbacks();
            }
            while (true)
            {
                lock (gate)
                lock (DesktopGlassCapturePool.GpuGate)
                    if (renderer is null || renderer.TryDiscardCompletedReadbacks()) return;
                if (deadline.Elapsed >= TimeSpan.FromSeconds(2))
                {
                    // A hung device must not retain the desktop shield or block Quit.
                    // Never pool a resource whose GPU completion is unconfirmed.
                    lock (gate)
                    {
                        retirementFailed = true;
                        Volatile.Write(ref diagnostic, "Readback retirement timeout; allocation discarded.");
                    }
                    return;
                }
                await completionDelay.WaitAsync(2).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException)
        {
            // A removed device cannot finish its event queries. Release the
            // allocation instead of returning invalid pixels/resources to idle.
            lock (gate) retirementFailed = true;
        }
    }

    public void Dispose()
    {
        RetireAsync().GetAwaiter().GetResult();
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            try
            {
                lock (DesktopGlassCapturePool.GpuGate)
                {
                    if (prepared is not null)
                    {
                        if (retirementFailed) prepared.Discard();
                        else prepared.Dispose();
                    }
                    else renderer?.Dispose();
                    prepared = null; renderer = null;
                }
            }
            finally { foreach (var capture in captures) capture.Lease.Dispose(); }
        }
    }

    private readonly record struct MonitorLease(GlassCaptureTile Tile, DesktopGlassCapturePool.Lease Lease);
}
