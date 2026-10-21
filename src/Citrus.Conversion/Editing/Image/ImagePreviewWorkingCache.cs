using System.Runtime.CompilerServices;
using Citrus.Core.Editing;
using ImageMagick;

[assembly: InternalsVisibleTo("Citrus.Conversion.Tests")]
namespace Citrus.Conversion.Editing.Image;

internal sealed record ImagePreviewDiagnostics(SizeI LastWorkingSize, long CacheBytes, int CacheEntries, long CacheHits, long CacheMisses, long CacheEvictions, int SourceEntries);
internal readonly record struct ImagePreviewWorkingKey(string SourceIdentity, ImageGeometry Geometry, SizeI Size);

/// <summary>Session-owned immutable RGBA bases, never adjusted pixels or retained native handles.
/// Call only under the renderer's serial lease. Budget covers all decoded bytes exactly.</summary>
internal sealed class ImagePreviewWorkingCache : IDisposable
{
    internal const long MaximumBytes = 64L * 1024 * 1024;
    internal const int MaximumEntries = 4;
    private sealed record Entry(ImagePreviewWorkingKey Key, byte[] Pixels);
    private readonly LinkedList<Entry> lru = new();
    internal long Bytes { get; private set; }
    internal int Count => lru.Count;
    internal long Hits { get; private set; }
    internal long Misses { get; private set; }
    internal long Evictions { get; private set; }
    internal MagickImage GetOrCreate(ImagePreviewWorkingKey key, Func<MagickImage> create, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        for (var node = lru.First; node is not null; node = node.Next)
        {
            if (node.Value.Key != key) continue;
            var entry = node.Value; lru.Remove(node); lru.AddLast(entry); Hits++;
            var hit = new MagickImage(entry.Pixels, new PixelReadSettings((uint)key.Size.Width, (uint)key.Size.Height, StorageType.Char, PixelMapping.RGBA));
            try { ct.ThrowIfCancellationRequested(); return hit; } catch { hit.Dispose(); throw; }
        }
        Misses++; var image = create();
        try
        {
            ct.ThrowIfCancellationRequested();
            long bytes = checked((long)image.Width * image.Height * 4);
            if (bytes <= MaximumBytes)
            {
                byte[] pixels = image.GetPixels().ToByteArray(PixelMapping.RGBA)!; ct.ThrowIfCancellationRequested();
                while (lru.Count >= MaximumEntries || Bytes + bytes > MaximumBytes) Remove(lru.First!);
                lru.AddLast(new Entry(key, pixels)); Bytes += bytes;
            }
            return image;
        }
        catch { image.Dispose(); throw; }
    }
    internal void RemoveSource(string identity)
    {
        for (var node = lru.First; node is not null;) { var next = node.Next; if (node.Value.Key.SourceIdentity == identity) Remove(node); node = next; }
    }
    private void Remove(LinkedListNode<Entry> node) { Bytes -= node.Value.Pixels.LongLength; lru.Remove(node); Evictions++; }
    public void Dispose() { lru.Clear(); Bytes = 0; }
}
