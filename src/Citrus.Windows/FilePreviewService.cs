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
    private static readonly SemaphoreSlim decoders = new(2);
    public async Task<ImageSource?> LoadAsync(string path, int pixelLimit, CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(pixelLimit, 1, 2048);
        await decoders.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<ImageSource?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".ico")) return null;
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length > 32 * 1024 * 1024) return null;
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var frame = decoder.Frames[0];
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
                    return bitmap;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException) { return null; }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { decoders.Release(); }
    }
}
