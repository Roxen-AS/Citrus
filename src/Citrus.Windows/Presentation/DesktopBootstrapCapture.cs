using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Citrus.Windows.Presentation;

/// <summary>
/// A fresh, bounded desktop ROI for the admitted wheel's first backing. The caller owns
/// capture exclusion and must erase the returned buffer after upload. Nothing is cached.
/// </summary>
internal static class DesktopBootstrapCapture
{
    private const int MaximumSide = 8192;
    private const long MaximumPixels = 16_777_216;

    internal static byte[]? TryCapture(GlassScene scene, int screenLeft, int screenTop, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (token.IsCancellationRequested) return null;
        using var scratch = new BootstrapCaptureScratch(scene.CaptureWidth, scene.CaptureHeight);
        return TryCapturePrepared(scene, screenLeft, screenTop, scratch, token);
    }

    internal static byte[]? TryCapturePrepared(GlassScene scene, int screenLeft, int screenTop,
        BootstrapCaptureScratch scratch, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(scratch);
        long left = (long)screenLeft - scene.PaddingPixels, top = (long)screenTop - scene.PaddingPixels;
        if (token.IsCancellationRequested || left < int.MinValue || left > int.MaxValue ||
            top < int.MinValue || top > int.MaxValue ||
            scratch.Width != scene.CaptureWidth || scratch.Height != scene.CaptureHeight ||
            !ValidBounds((int)left, (int)top, scene.CaptureWidth, scene.CaptureHeight))
        { scratch.Clear(); return null; }
        nint screen = 0;
        bool compositionOwnsClear = false;
        long started = Stopwatch.GetTimestamp();
        try
        {
            var monitors = DesktopGlassCapturePool.Monitors();
            if (token.IsCancellationRequested) return null;
            screen = GetDC(0);
            if (screen == 0) return null;
            double preparationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            compositionOwnsClear = true;
            byte[]? result = CapturePreparedTiles((int)left, (int)top, scratch, monitors,
                tile => scratch.Blit(screen, tile), token);
            scratch.LastTiming = scratch.LastTiming with { PreparationMilliseconds = preparationMs };
            return result;
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or
            ArgumentException or OverflowException or OperationCanceledException)
        { return null; }
        finally { if (!compositionOwnsClear) scratch.Clear(); if (screen != 0) ReleaseDC(0, screen); }
    }

    /// <summary>One prepared native destination and one managed readback; tests only write generated DIB pixels.</summary>
    internal static byte[]? CapturePreparedTiles(int screenLeft, int screenTop, BootstrapCaptureScratch scratch,
        IReadOnlyList<GlassMonitor> monitors, Func<GlassCaptureTile, bool> copyTile, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scratch); ArgumentNullException.ThrowIfNull(monitors); ArgumentNullException.ThrowIfNull(copyTile);
        byte[]? result = null;
        bool complete = false;
        bool graphicsFlushed = false;
        double blitMs = 0, copyMs = 0;
        try
        {
            if (token.IsCancellationRequested || monitors.Count > 64 || !ValidBounds(screenLeft, screenTop, scratch.Width, scratch.Height)) return null;
            var tiles = StudioGlassTiling.Plan(screenLeft, screenTop, scratch.Width, scratch.Height, monitors);
            if (tiles.Count == 0) return null;
            long started = Stopwatch.GetTimestamp();
            foreach (var tile in tiles) if (token.IsCancellationRequested || !copyTile(tile)) return null;
            graphicsFlushed = BootstrapCaptureScratch.Flush();
            if (!graphicsFlushed || token.IsCancellationRequested) return null;
            blitMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            started = Stopwatch.GetTimestamp();
            result = scratch.CopyPixels();
            if (!ExtendEdges(result, scratch.Width, scratch.Height, tiles, token)) return null;
            for (int p = 3; p < result.Length; p += 4) result[p] = 255;
            if (token.IsCancellationRequested) return null;
            copyMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            complete = true; return result;
        }
        finally
        {
            long clearStarted = Stopwatch.GetTimestamp();
            // Cancellation/failure can interrupt a sequence of batched BitBlt
            // calls. Finish those writes before erasing reusable native storage.
            if (!graphicsFlushed) BootstrapCaptureScratch.Flush();
            scratch.Clear();
            if (!complete && result is not null) Array.Clear(result);
            scratch.LastTiming = new(0, blitMs, copyMs, Stopwatch.GetElapsedTime(clearStarted).TotalMilliseconds);
        }
    }

    /// <summary>Physical tile composition seam. Tests supply generated pixels, never a desktop DC.</summary>
    internal static byte[]? Capture(int screenLeft, int screenTop, int width, int height,
        IReadOnlyList<GlassMonitor> monitors, Func<int, int, int, int, byte[]?> readTile, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(readTile);
        if (token.IsCancellationRequested || monitors.Count > 64 || !ValidBounds(screenLeft, screenTop, width, height)) return null;
        var tiles = StudioGlassTiling.Plan(screenLeft, screenTop, width, height, monitors);
        if (tiles.Count == 0) return null;
        var result = new byte[checked(width * height * 4)];
        bool complete = false;
        try
        {
            foreach (var tile in tiles)
            {
                if (token.IsCancellationRequested) return null;
                byte[]? pixels = null;
                try
                {
                    int x = checked(tile.Monitor.Left + tile.SourceLeft), y = checked(tile.Monitor.Top + tile.SourceTop);
                    pixels = readTile(x, y, tile.Width, tile.Height);
                    if (token.IsCancellationRequested || pixels is null || pixels.Length != checked(tile.Width * tile.Height * 4)) return null;
                    int rowBytes = checked(tile.Width * 4);
                    for (int row = 0; row < tile.Height; row++)
                        Buffer.BlockCopy(pixels, row * rowBytes, result,
                            ((tile.DestinationTop + row) * width + tile.DestinationLeft) * 4, rowBytes);
                }
                finally { if (pixels is not null) Array.Clear(pixels); }
            }
            if (!ExtendEdges(result, width, height, tiles, token)) return null;
            for (int p = 3; p < result.Length; p += 4) result[p] = 255;
            if (token.IsCancellationRequested) return null;
            complete = true;
            return result;
        }
        finally { if (!complete) Array.Clear(result); }
    }

    private static bool ValidBounds(int left, int top, int width, int height) =>
        width is > 0 and <= MaximumSide && height is > 0 and <= MaximumSide &&
        (long)width * height <= MaximumPixels &&
        (long)left + width <= int.MaxValue && (long)top + height <= int.MaxValue;

    private static bool ExtendEdges(byte[] pixels, int width, int height,
        IReadOnlyList<GlassCaptureTile> tiles, CancellationToken token)
    {
        // The usual path is wholly on one monitor; no per-pixel coverage search is needed.
        if (tiles.Count == 1 && tiles[0].DestinationLeft == 0 && tiles[0].DestinationTop == 0 &&
            tiles[0].Width == width && tiles[0].Height == height) return !token.IsCancellationRequested;
        for (int y = 0; y < height; y++)
        {
            if (token.IsCancellationRequested) return false;
            for (int x = 0; x < width; x++)
            {
                long nearest = long.MaxValue;
                int sourceX = x, sourceY = y;
                foreach (var tile in tiles)
                {
                    int candidateX = Math.Clamp(x, tile.DestinationLeft, tile.DestinationLeft + tile.Width - 1);
                    int candidateY = Math.Clamp(y, tile.DestinationTop, tile.DestinationTop + tile.Height - 1);
                    long dx = (long)x - candidateX, dy = (long)y - candidateY;
                    long distance = dx * dx + dy * dy;
                    if (distance >= nearest) continue;
                    nearest = distance; sourceX = candidateX; sourceY = candidateY;
                    if (distance == 0) break;
                }
                if (nearest == 0) continue;
                int source = (sourceY * width + sourceX) * 4, destination = (y * width + x) * 4;
                pixels[destination] = pixels[source]; pixels[destination + 1] = pixels[source + 1];
                pixels[destination + 2] = pixels[source + 2];
            }
        }
        return true;
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
}
