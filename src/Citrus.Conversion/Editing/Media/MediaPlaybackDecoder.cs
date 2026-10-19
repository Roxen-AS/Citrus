using System.Runtime.InteropServices;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Media;

/// <summary>One sequential child per clip, bounded frame assembly, backpressure from the consumer.
/// The caller must not acquire PreviewWorkScheduler around this entry point.</summary>
public sealed class MediaPlaybackDecoder(MediaProcess process)
{
    public async Task StreamVideoAsync(MediaPlaybackPlan plan, Func<PreviewFrame, CancellationToken, ValueTask> consume, CancellationToken ct)
    {
        foreach (var segment in plan.Video)
        {
            var graph = segment.Graph;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct, graph.Owner.LifetimeToken);
            using var session = await graph.Owner.AcquireDecodeAsync(cancel.Token).ConfigureAwait(false);
            using var shared = await PreviewWorkScheduler.AcquireAsync(cancel.Token).ConfigureAwait(false);
            using var readLocks = new SourceReadLocks(graph);
            foreach (var source in graph.Sources) await source.VerifyAsync(cancel.Token).ConfigureAwait(false);
            await using var output = new FrameOutput(segment, consume);
            long expected = checked((long)output.FrameBytes * segment.Frames.Length);
            long actual = await process.RunBinaryAsync(graph.Arguments, output, expected, cancel.Token).ConfigureAwait(false);
            if (actual != expected || !output.Complete) throw new MediaProcessException("Unexpected EOF in streaming BGRA frames.");
            foreach (var source in graph.Sources) await source.VerifyAsync(cancel.Token).ConfigureAwait(false);
        }
    }
    // On Windows these handles deny stage mutation/deletion during publication of streamed pixels.
    // They permit the decoder's read opens and live through the post-decode hash check.
    private sealed class SourceReadLocks : IDisposable
    {
        private readonly List<FileStream> files = [];
        public SourceReadLocks(MediaGraphPlan graph)
        {
            try { foreach (var source in graph.Sources) files.Add(new(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read)); }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var file in files) file.Dispose(); files.Clear(); }
    }
    private sealed class FrameOutput(VideoPlaybackSegment segment, Func<PreviewFrame, CancellationToken, ValueTask> consume) : Stream
    {
        private byte[]? frame;
        private int filled, index;
        public int FrameBytes { get; } = checked(segment.Graph.VideoSize!.Value.Width * segment.Graph.VideoSize.Value.Height * 4);
        public bool Complete => filled == 0 && index == segment.Frames.Length;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            while (!buffer.IsEmpty)
            {
                if (index >= segment.Frames.Length) throw new MediaProcessException("Overlong streaming frame payload.");
                frame ??= new byte[FrameBytes];
                int count = Math.Min(frame.Length - filled, buffer.Length);
                buffer[..count].CopyTo(frame.AsMemory(filled)); filled += count; buffer = buffer[count..];
                if (filled != frame.Length) continue;
                var stamp = segment.Frames[index++]; var graph = segment.Graph; var size = graph.VideoSize!.Value;
                // Transfer the completed array; never mutate/reuse it after publishing its immutable view.
                var pixels = ImmutableCollectionsMarshal.AsImmutableArray(frame);
                frame = null; filled = 0;
                await consume(new(graph.Revision, graph.Generation, size, checked(size.Width * 4), pixels, stamp.Time, stamp.Identity, []), ct).ConfigureAwait(false);
            }
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
