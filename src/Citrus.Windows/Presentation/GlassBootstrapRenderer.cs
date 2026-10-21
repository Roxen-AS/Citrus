using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Citrus.Windows.Presentation;

internal readonly record struct GlassBootstrapWork(double CaptureMilliseconds, double RenderMilliseconds,
    double CompletionMilliseconds, bool PreparedResources)
{
    internal double ResourcePreparationMilliseconds { get; init; }
    internal double CapturePreparationMilliseconds { get; init; }
    internal double CaptureBlitMilliseconds { get; init; }
    internal double CaptureCopyMilliseconds { get; init; }
    internal double CaptureClearMilliseconds { get; init; }
}

internal sealed record GlassBootstrapResult(byte[] Pixels, GlassBootstrapWork Work);

/// <summary>One admission snapshot through the normal GPU material; never a resident desktop cache.</summary>
internal static class GlassBootstrapRenderer
{
    /// <summary>Compatibility for isolated worker-side native proofs. Never call while holding GpuGate.</summary>
    internal static byte[]? TryRender(GlassScene scene, int screenX, int screenY, CancellationToken token,
        out GlassBootstrapWork work)
    {
        if (Monitor.IsEntered(DesktopGlassCapturePool.GpuGate))
            throw new InvalidOperationException("Bootstrap completion cannot wait while holding the shared GPU gate.");
        var result = TryRenderAsync(scene, screenX, screenY, token).GetAwaiter().GetResult();
        work = result?.Work ?? default;
        return result?.Pixels;
    }

    internal static async Task<GlassBootstrapResult?> TryRenderAsync(GlassScene scene, int screenX, int screenY,
        CancellationToken token, bool navigationVisible = true)
    {
        byte[]? source = null, output = null;
        StudioGlassRuntime.Lease? prepared = null;
        bool completed = false;
        using var completionDelay = new GlassRenderDelay();
        try
        {
            token.ThrowIfCancellationRequested();
            long started = Stopwatch.GetTimestamp();
            prepared = StudioGlassRuntime.Shared.Rent(scene);
            double prepareMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            started = Stopwatch.GetTimestamp();
            source = DesktopBootstrapCapture.TryCapturePrepared(scene, screenX, screenY, prepared.Scratch, token);
            if (source is null) return null;
            double captureMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var capture = prepared.Scratch.LastTiming;
            token.ThrowIfCancellationRequested();
            output = new byte[checked(scene.PixelWidth * scene.PixelHeight * 4)];
            long submitted;
            double renderMs;
            lock (DesktopGlassCapturePool.GpuGate)
            {
                token.ThrowIfCancellationRequested();
                if (!prepared.Renderer.TrySubmitPixels(source, scene.CaptureWidth, scene.CaptureHeight, 0, 0,
                    new(NavigationVisible: navigationVisible), 1))
                    throw new InvalidOperationException("A freshly rented bootstrap renderer already has pending work.");
                submitted = Stopwatch.GetTimestamp();
                renderMs = prepared.Renderer.LastRenderMilliseconds;
            }
            // Upload consumes the CPU pixels before returning; neither completion
            // polling nor live capture needs a retained desktop source array.
            Array.Clear(source); source = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool ready;
                lock (DesktopGlassCapturePool.GpuGate) ready = prepared.Renderer.TryComplete(output, out _);
                if (ready)
                {
                    completed = true;
                    var work = new GlassBootstrapWork(captureMs, renderMs,
                        Stopwatch.GetElapsedTime(submitted).TotalMilliseconds, prepared.Prepared)
                    {
                        ResourcePreparationMilliseconds = prepareMs,
                        CapturePreparationMilliseconds = capture.PreparationMilliseconds,
                        CaptureBlitMilliseconds = capture.BlitMilliseconds,
                        CaptureCopyMilliseconds = capture.CopyMilliseconds,
                        CaptureClearMilliseconds = capture.ClearMilliseconds
                    };
                    return new(output, work);
                }
                if (Stopwatch.GetElapsedTime(submitted) >= TimeSpan.FromSeconds(2))
                    throw new InvalidOperationException("Bootstrap GPU completion timed out.");
                await completionDelay.WaitAsync(1, token).ConfigureAwait(false);
            }
        }
        finally
        {
            if (source is not null) Array.Clear(source);
            if (!completed && output is not null) Array.Clear(output);
            // Runtime return invalidates/drains unfinished tickets off the GPU
            // lock before clearing/pooling. Genuine failure discards allocation.
            if (prepared is not null)
            {
                try { prepared.Dispose(); }
                catch (Exception error) when (error is ExternalException or InvalidOperationException) { prepared.Discard(); }
            }
        }
    }
}
