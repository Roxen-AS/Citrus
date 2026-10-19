using System.Collections.Immutable;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Tests;

public sealed class WaveformTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(),"Citrus-wave-"+Guid.NewGuid().ToString("N"));
    public WaveformTests()=>Directory.CreateDirectory(directory);
    [Fact]
    public async Task Waveform_matches_known_sine_silence_and_channel_extrema_with_exact_edges()
    {
        string path=Path.Combine(directory,"samples.pcm");
        short[] data=Enumerable.Range(0,101).SelectMany(i=>new short[]{i<50?(short)(24000*Math.Sin(i*Math.PI/4)): (short)0,i<50?(short)-12000:(short)0}).ToArray();
        using(var w=new BinaryWriter(File.Create(path))) foreach(var value in data) w.Write(value);
        var buckets=await new WaveformService().BuildAsync(new(path,48000,2,101),7,default);
        Assert.Equal(7,buckets.Length);
        for(int i=0;i<7;i++)
        {
            long first=i*101L/7,last=(i+1)*101L/7;
            Assert.Equal(first,buckets[i].FirstSample); Assert.Equal(last,buckets[i].LastSample);
            var samples=data.Skip((int)first*2).Take((int)(last-first)*2).ToArray();
            Assert.Equal(samples.Min()/32768f,buckets[i].Minimum); Assert.Equal(samples.Max()/32768f,buckets[i].Maximum);
        }
    }
    [Fact]
    public async Task Waveform_rejects_truncated_pcm_and_honors_cancellation()
    {
        string path=Path.Combine(directory,"bad.pcm"); File.WriteAllBytes(path,[255,128,0]);
        var service=new WaveformService();
        await Assert.ThrowsAsync<IOException>(()=>service.BuildAsync(new(path,48000,1,2),10,default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.BuildAsync(new(path,48000,1,2),10,new CancellationToken(true)));
    }
    [Fact]
    public async Task Real_pcm_trim_concat_preserves_samples_and_exact_duration_without_png_or_lavfi()
    {
        var process=new MediaProcess(Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG")??"/usr/bin/ffmpeg",Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE")??"/usr/bin/ffprobe");
        string raw=Path.Combine(directory,"sine.pcm"),wav=Path.Combine(directory,"sine.wav");
        short[] samples=Enumerable.Range(0,4800).Select(i=>(short)(12000*Math.Sin(i*Math.PI/24))).ToArray();
        using(var writer=new BinaryWriter(File.Create(raw))) foreach(short sample in samples) writer.Write(sample);
        await process.RunAsync(["-f","s16le","-ar","48000","-ac","1","-i",raw,"-c:a","pcm_s16le",wav],null,default);
        var source=(await new SourceSnapshotService().CaptureAsync([wav],default))[0].Snapshot!;
        await using var probe=new MediaProbe(process,new SourceSnapshotService(),Path.Combine(directory,"probe"));
        var info=await probe.InspectAsync(source,default);
        var clip=new MediaClip(Guid.NewGuid(),source.Id,null,info.Audio[0].Index,new(new(1,100),new(6,100)),1);
        var recipe=new AudioRecipe([clip,clip with {Id=Guid.NewGuid()}],48000,new(["FC"],["FC"],[1],false),[],null,[]);
        var doc=new EditorDocument(1,1,[source],[],recipe,new("wav",null,"audio",new QualityGoal(80),MetadataPolicy.StripAll,new()));
        var capabilities=await process.DiscoverCapabilitiesAsync(default);
        var plan=new MediaGraphCompiler(probe,capabilities).Compile(doc,RenderPurpose.Preview,new(1,new(16,8),null,null,null),probe.StagedSources);
        string output=Path.Combine(directory,"decoded.pcm");
        var pcm=await new MediaBinaryDecoder(process).DecodeAudioAsync(plan,output,default);
        Assert.Equal(4800,pcm.SampleFrames); Assert.Equal(new RationalTime(1,10),plan.Duration);
        byte[] bytes=File.ReadAllBytes(output);
        short[] decoded=Enumerable.Range(0,bytes.Length/2).Select(i=>System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i*2,2))).ToArray();
        Assert.Equal(samples.Skip(480).Take(2400).Concat(samples.Skip(480).Take(2400)),decoded);
        if(!OperatingSystem.IsWindows())
        {
            string shortOutput=Path.Combine(directory,"short.sh"),badPcm=Path.Combine(directory,"short.pcm");
            File.WriteAllText(shortOutput,"#!/bin/sh\nprintf '\\377\\200'\n");
            File.SetUnixFileMode(shortOutput,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            await Assert.ThrowsAsync<MediaProcessException>(()=>new MediaBinaryDecoder(new(shortOutput,shortOutput)).DecodeAudioAsync(plan,badPcm,default));
            Assert.False(File.Exists(badPcm));
            File.WriteAllText(shortOutput,"#!/bin/sh\nsleep 60 &\nwait\n");
            using var cancel=new CancellationTokenSource(300);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new MediaBinaryDecoder(new(shortOutput,shortOutput)).DecodeAudioAsync(plan,badPcm,cancel.Token));
            Assert.False(File.Exists(badPcm));
            File.WriteAllBytes(badPcm,[1,2,3]);
            await Assert.ThrowsAsync<IOException>(()=>new MediaBinaryDecoder(process).DecodeAudioAsync(plan,badPcm,default));
            Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(badPcm));
        }
        Assert.NotEmpty(await new WaveformService().BuildAsync(pcm,13,default));
    }
    [Theory]
    [InlineData(0.0,0.1,1.0)]
    [InlineData(0.1,0.6,1.0)]
    [InlineData(0.1,0.6,0.25)]
    [InlineData(0.1,0.6,4.0)]
    public async Task Audio_stream_offset_keeps_silence_and_speed_has_exact_sample_count(double from,double to,double speed)
    {
        var process=new MediaProcess(Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG")??"/usr/bin/ffmpeg",Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE")??"/usr/bin/ffprobe");
        string raw=Path.Combine(directory,"constant.pcm"),mixed=Path.Combine(directory,"offset.mov");
        using(var writer=new BinaryWriter(File.Create(raw))) for(int i=0;i<9600;i++) writer.Write((short)10000);
        await process.RunAsync(["-copyts","-itsoffset","0.25","-f","s16le","-ar","48000","-ac","1","-i",raw,
            "-filter_complex","color=black:s=16x8:r=25:d=1[v]","-map","[v]","-map","0:a:0","-c:v","mpeg4","-c:a","pcm_s16le",mixed],null,default);
        var source=(await new SourceSnapshotService().CaptureAsync([mixed],default))[0].Snapshot!;
        await using var probe=new MediaProbe(process,new SourceSnapshotService(),Path.Combine(directory,"probe"));
        var info=await probe.InspectAsync(source,default);
        Assert.Equal(new RationalTime(0,1),info.PresentationOrigin); Assert.Equal(12000,info.Audio[0].RawStartPts);
        var clip=new MediaClip(Guid.NewGuid(),source.Id,null,info.Audio[0].Index,new(new((long)(from*10),10),new((long)(to*10),10)),speed);
        var doc=new EditorDocument(1,1,[source],[],new AudioRecipe([clip],48000,new(["FC"],["FC"],[1],false),[],null,[]),new("wav",null,"audio",new QualityGoal(80),MetadataPolicy.StripAll,new()));
        var plan=new MediaGraphCompiler(probe,await process.DiscoverCapabilitiesAsync(default)).Compile(doc,RenderPurpose.Preview,new(1,new(16,8),null,null,null),probe.StagedSources);
        var pcm=await new MediaBinaryDecoder(process).DecodeAudioAsync(plan,Path.Combine(directory,"out.pcm"),default);
        Assert.Equal((long)Math.Round((to-from)*48000/speed),pcm.SampleFrames);
        var bytes=await File.ReadAllBytesAsync(pcm.Path);
        var samples=Enumerable.Range(0,bytes.Length/2).Select(i=>System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i*2,2))).ToArray();
        if(to<=.25) Assert.All(samples,s=>Assert.Equal(0,s));
        else if(speed==1)
        {
            Assert.All(samples.Take(7200),s=>Assert.Equal(0,s));
            Assert.All(samples.Skip(7200).Take(9600),s=>Assert.Equal(10000,s));
            Assert.All(samples.Skip(16800),s=>Assert.Equal(0,s));
        }
        else Assert.Contains(samples,s=>s>9000);
    }
    public void Dispose()=>Directory.Delete(directory,true);
}
