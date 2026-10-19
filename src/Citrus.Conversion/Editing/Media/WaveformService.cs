using System.Buffers.Binary;
using System.Collections.Immutable;

namespace Citrus.Conversion.Editing.Media;

public sealed class WaveformService
{
    private static readonly SemaphoreSlim Workers=new(2);
    /// <summary>Single bounded streaming pass, O(pixelWidth) memory. No resident PCM cache or synthetic values.</summary>
    public async Task<ImmutableArray<WaveformBucket>> BuildAsync(AudioPcmInfo pcm,int pixelWidth,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(pixelWidth is <1 or >16384||pcm.SampleRate is <8000 or >192000||pcm.Channels is <1 or >64||pcm.SampleFrames<0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        long expected=checked(pcm.SampleFrames*pcm.Channels*2);
        if(expected>MediaBinaryDecoder.MaximumPcmBytes) throw new NotSupportedException("PCM exceeds the preview cache limit.");
        await Workers.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var file=new FileStream(pcm.Path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
            if(file.Length!=expected) throw new IOException("PCM length differs from its sample-frame declaration.");
            if(pcm.SampleFrames==0) return [];
            int count=(int)Math.Min(pixelWidth,pcm.SampleFrames),frameBytes=pcm.Channels*2;
            var buckets=ImmutableArray.CreateBuilder<WaveformBucket>(count);
            byte[] buffer=new byte[65536/frameBytes*frameBytes];
            for(int bucket=0;bucket<count;bucket++)
            {
                long first=bucket*pcm.SampleFrames/count,last=(bucket+1)*pcm.SampleFrames/count;
                long remaining=(last-first)*frameBytes; short minimum=short.MaxValue,maximum=short.MinValue;
                while(remaining>0)
                {
                    ct.ThrowIfCancellationRequested();
                    int bytes=(int)Math.Min(buffer.Length,remaining);
                    await file.ReadExactlyAsync(buffer.AsMemory(0,bytes),ct).ConfigureAwait(false);
                    for(int i=0;i<bytes;i+=2) { short value=BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i,2)); minimum=Math.Min(minimum,value); maximum=Math.Max(maximum,value); }
                    remaining-=bytes;
                }
                buckets.Add(new(minimum/32768f,maximum/32768f,first,last));
            }
            if(file.Length!=expected) throw new IOException("PCM changed during waveform preparation.");
            ct.ThrowIfCancellationRequested(); return buckets.MoveToImmutable();
        }
        finally { Workers.Release(); }
    }
}
