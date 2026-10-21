using Citrus.Core;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Citrus.Windows;

/// <summary>Local raster previews only; all I/O and codec work stays on bounded workers.</summary>
public interface IFilePreviewService
{
    Task<ImageSource?> LoadAsync(string path, int pixelLimit, CancellationToken cancellationToken);
}

public sealed class FilePreviewService : IFilePreviewService
{
    public async Task<ImageSource?> LoadAsync(string path, int pixelLimit, CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(pixelLimit, 1, 2048);
        var lease = await Citrus.Core.Editing.PreviewWorkScheduler.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<ImageSource?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".ico")) return null;
                try
                {
                    // Admission can be stale after waiting for a worker, and workspace callers have no OLE admission.
                    // Metadata only: never open offline/recall placeholders or directories to inspect preview bytes.
                    if (!DropValidation.AttributesAllowed(File.GetAttributes(path), OperationMode.Convert)) return null;
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length > 32 * 1024 * 1024) return null;
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var frame = decoder.Frames[0];
                    int orientation = ReadOrientation(frame.Metadata as BitmapMetadata);
                    int width = frame.PixelWidth, height = frame.PixelHeight;
                    if (width <= 0 || height <= 0 || (long)width * height > 100_000_000) return null;
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    if (width >= height) bitmap.DecodePixelWidth = Math.Min(limit, width);
                    else bitmap.DecodePixelHeight = Math.Min(limit, height);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (orientation == 1) return bitmap;
                    // WIC exposes raw camera pixels. Orthogonal transforms preserve the decode bound,
                    // normalize reflected/rotated bounds, and exchange dimensions for orientations 5–8.
                    var transform = orientation switch
                    {
                        2 => new MatrixTransform(-1, 0, 0, 1, 0, 0),
                        3 => new MatrixTransform(-1, 0, 0, -1, 0, 0),
                        4 => new MatrixTransform(1, 0, 0, -1, 0, 0),
                        5 => new MatrixTransform(0, 1, 1, 0, 0, 0),
                        6 => new MatrixTransform(0, 1, -1, 0, 0, 0),
                        7 => new MatrixTransform(0, -1, -1, 0, 0, 0),
                        _ => new MatrixTransform(0, -1, 1, 0, 0, 0)
                    };
                    transform.Freeze();
                    var oriented = new TransformedBitmap(bitmap, transform);
                    oriented.Freeze();
                    cancellationToken.ThrowIfCancellationRequested();
                    return oriented;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException) { return null; }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { lease.Dispose(); }
    }

    private static int ReadOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null) return 1;
        foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (metadata.GetQuery(query) is ushort orientation && orientation is >= 1 and <= 8) return orientation;
            }
            catch (Exception error) when (error is NotSupportedException or ArgumentException) { }
        }
        return 1;
    }
}
