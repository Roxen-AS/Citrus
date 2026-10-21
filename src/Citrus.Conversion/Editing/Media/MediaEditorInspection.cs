using System.Collections.Immutable;
using System.Globalization;
using Citrus.Core.Editing;
using ImageMagick;

namespace Citrus.Conversion.Editing.Media;

public sealed partial class MediaEditorEngine
{
    public async Task<PreviewFrame> RenderVisualizationPreviewAsync(EditorDocument document,PreviewRequest request,CancellationToken ct)
    {
        if(document.Recipe is not VisualizerRecipe visual||request.Bounds.Width is <1 or >2048||request.Bounds.Height is <1 or >2048)throw new ArgumentException("Choose a bounded visualization preview.");
        if(visual.BackgroundImageId is not null)throw new NotSupportedException("Image backgrounds are not available in this visualization preset.");
        string scope=Path.Combine(Path.GetTempPath(),"citrus-visualizer-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scope);
        try
        {
            await using var probe=new MediaProbe(Process,Sources,Path.Combine(scope,"sources"));
            foreach(var source in document.Sources)await probe.InspectAsync(source,ct).ConfigureAwait(false);
            var audioDoc=document with {Recipe=visual.Audio,Export=document.Export with {Compression=new QualityGoal(85),FormatId="wav"}};
            var graph=new MediaGraphCompiler(probe,Capabilities).Compile(audioDoc,RenderPurpose.Preview,new(request.Generation,new(32,32),null,null,null),probe.StagedSources);
            var pcm=await new MediaBinaryDecoder(Process).DecodeAudioAsync(graph,Path.Combine(scope,"audio.pcm"),ct).ConfigureAwait(false);
            double scale=Math.Min(request.Bounds.Width/(double)visual.Size.Width,request.Bounds.Height/(double)visual.Size.Height);
            var size=new SizeI(Math.Max(1,(int)(visual.Size.Width*scale)),Math.Max(1,(int)(visual.Size.Height*scale)));
            string filter=visual.Mode==VisualizerMode.Spectrum?$"showspectrumpic=s={size.Width}x{size.Height}:legend=disabled":$"showwavespic=s={size.Width}x{size.Height}:colors=0x{visual.Foreground.R:X2}{visual.Foreground.G:X2}{visual.Foreground.B:X2}";
            using var pixels=new MemoryStream();
            long actual=await Process.RunBinaryAsync(["-f","s16le","-ar",pcm.SampleRate.ToString(CultureInfo.InvariantCulture),"-ac",pcm.Channels.ToString(CultureInfo.InvariantCulture),"-i",pcm.Path,"-filter_complex",$"[0:a]{filter},format=bgra[out]","-map","[out]","-frames:v","1","-c:v","rawvideo","-pix_fmt","bgra","-f","rawvideo","pipe:1"],pixels,checked(size.Width*size.Height*4L),ct).ConfigureAwait(false);
            if(actual!=size.Width*size.Height*4L)throw new InvalidDataException("Incomplete visualization preview.");
            using var image=new MagickImage(pixels.ToArray(),new PixelReadSettings((uint)size.Width,(uint)size.Height,StorageType.Char,PixelMapping.BGRA));
            image.BackgroundColor=new MagickColor(visual.Background.R,visual.Background.G,visual.Background.B,visual.Background.A);image.Alpha(AlphaAction.Remove);
            return new(document.Revision,request.Generation,size,size.Width*4,[..image.GetPixels().ToByteArray(PixelMapping.BGRA)!],null,null,[]);
        }
        finally{if(Directory.Exists(scope))Directory.Delete(scope,true);}
    }
    public async Task<ImmutableArray<MetadataField>> ReadMetadataAsync(SourceSnapshot source,CancellationToken ct)
    {
        string scope=Path.Combine(Path.GetTempPath(),"citrus-media-tags-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=await Sources.StageVerifiedAsync([source],scope,ct).ConfigureAwait(false);
            using var json=await Process.ProbeJsonAsync(["-show_streams","-show_format","-show_chapters","-show_programs","-of","json",..MediaProcess.InputArguments(paths[source.Id])],2*1024*1024,ct).ConfigureAwait(false);
            var result=ImmutableArray.CreateBuilder<MetadataField>();
            if(json.RootElement.TryGetProperty("format",out var format))
            {
                foreach(var property in format.EnumerateObject().Where(p=>p.Name!="tags"&&p.Name!="filename"))
                    result.Add(new("File",property.Name,property.Name.Replace('_',' '),new TextValue(property.Value.ToString()),false));
            }
            var chapterStreams=MediaMetadataScopes.ChapterStreams(json.RootElement);
            foreach(var tag in MediaMetadataScopes.Tags(json.RootElement))
            {
                bool mutable=MediaMetadataScopes.Writable(tag.Scope,tag.Key)&&!(MediaMetadataScopes.Scope(tag.Scope,out char kind,out int index)&&kind=='s'&&chapterStreams.Contains(index));
                result.Add(new(tag.Scope,tag.Key,tag.Key.Replace('_',' '),new TextValue(tag.Value),mutable,mutable));
            }
            foreach(var stream in json.RootElement.GetProperty("streams").EnumerateArray())
            {
                string group="Stream "+stream.GetProperty("index").GetInt32();
                foreach(var field in stream.EnumerateObject().Where(p=>p.Value.ValueKind is System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Number))
                    result.Add(new(group,field.Name,field.Name.Replace('_',' '),new TextValue(field.Value.ToString()),false));
            }
            // Always offer common supported fields, including files which start without descriptive tags.
            foreach(string key in new[]{"title","artist","album","comment","genre","copyright"})
                if(!result.Any(f=>f.Scope=="format"&&f.Key.Equals(key,StringComparison.OrdinalIgnoreCase))) result.Add(new("format",key,key,new TextValue(""),true,true));
            return result.ToImmutable();
        }
        finally {if(Directory.Exists(scope)) Directory.Delete(scope,true);}
    }
    public async Task<ImmutableArray<ImmutableArray<WaveformBucket>>> BuildWaveformsAsync(EditorDocument document,int width,CancellationToken ct)
    {
        string scope=Path.Combine(Path.GetTempPath(),"citrus-media-waveforms-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scope);
        try
        {
            await using var probe=new MediaProbe(Process,Sources,Path.Combine(scope,"sources"));
            foreach(var source in document.Sources) await probe.InspectAsync(source,ct).ConfigureAwait(false);
            var audio=document.Recipe switch
            {
                AudioRecipe a=>a,VisualizerRecipe v=>v.Audio,
                VideoRecipe v when v.Clips[0].AudioStream is {} index=>new AudioRecipe(v.Clips,probe.GetInfo(v.Clips[0].SourceId).Audio.First(a=>a.Index==index).SampleRate,IdentityMatrix(probe.GetInfo(v.Clips[0].SourceId).Audio.First(a=>a.Index==index).Channels),[],null,[]),
                _=>null
            };
            if(audio is null) return [];
            var doc=document with {Recipe=audio,Export=document.Export with {Compression=new QualityGoal(85)}};
            var plan=new MediaGraphCompiler(probe,Capabilities).Compile(doc,RenderPurpose.Preview,new(0,new(32,32),null,null,null),probe.StagedSources);
            var pcm=await new MediaBinaryDecoder(Process).DecodeAudioAsync(plan,Path.Combine(scope,"waveform.pcm"),ct).ConfigureAwait(false);
            var rows=ImmutableArray.CreateBuilder<ImmutableArray<WaveformBucket>>();
            for(int channel=0;channel<pcm.Channels;channel++) rows.Add(await ChannelWaveform(pcm,width,channel,ct).ConfigureAwait(false));
            return rows.ToImmutable();
        }
        finally {if(Directory.Exists(scope)) Directory.Delete(scope,true);}
    }
    private static async Task<ImmutableArray<WaveformBucket>> ChannelWaveform(AudioPcmInfo pcm,int width,int channel,CancellationToken ct)
    {
        if(width is <1 or >16384) throw new ArgumentOutOfRangeException(nameof(width));
        int count=(int)Math.Min(width,pcm.SampleFrames),frameBytes=checked(pcm.Channels*2);
        if(count==0) return [];
        var result=ImmutableArray.CreateBuilder<WaveformBucket>(count);
        await using var file=new FileStream(pcm.Path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        byte[] buffer=new byte[65536/frameBytes*frameBytes];
        for(int bucket=0;bucket<count;bucket++)
        {
            long first=bucket*pcm.SampleFrames/count,last=(bucket+1)*pcm.SampleFrames/count,remaining=(last-first)*frameBytes;
            short min=short.MaxValue,max=short.MinValue;
            while(remaining>0)
            {
                int bytes=(int)Math.Min(buffer.Length,remaining);await file.ReadExactlyAsync(buffer.AsMemory(0,bytes),ct).ConfigureAwait(false);
                for(int offset=channel*2;offset<bytes;offset+=frameBytes) {short sample=System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset,2));min=Math.Min(min,sample);max=Math.Max(max,sample);}
                remaining-=bytes;
            }
            result.Add(new(min/32768f,max/32768f,first,last));
        }
        return result.MoveToImmutable();
    }
    public async Task<TimeRange> FindAudibleRangeAsync(EditorDocument document,CancellationToken ct)
    {
        var channels=await BuildWaveformsAsync(document,Math.Min(16384,Math.Max(1,(int)(Duration(document)*1000))),ct).ConfigureAwait(false);
        if(channels.IsEmpty) throw new NotSupportedException("This source has no audio to inspect.");
        var buckets=channels[0];
        double threshold=Math.Pow(10,-45d/20);
        int first=0,last=buckets.Length-1;
        while(first<buckets.Length&&channels.All(c=>Math.Max(Math.Abs(c[first].Minimum),Math.Abs(c[first].Maximum))<threshold)) first++;
        while(last>=first&&channels.All(c=>Math.Max(Math.Abs(c[last].Minimum),Math.Abs(c[last].Maximum))<threshold)) last--;
        if(first>last) throw new InvalidOperationException("No audible samples were found. The trim would be empty.");
        int rate=(document.Recipe as AudioRecipe)?.SampleRate??48000;
        double firstSound=buckets[first].FirstSample/(double)rate,lastSound=buckets[last].LastSample/(double)rate;
        double start=firstSound<.2?0:Math.Max(0,firstSound-.020),end=Duration(document)-lastSound<.2?Duration(document):Math.Min(Duration(document),lastSound+.020);
        return new(Time(start),Time(end));
    }
    public static double Duration(EditorDocument document)=>Clips(document.Recipe).Sum(c=>(MediaEffectFilters.Seconds(c.Range.End)-MediaEffectFilters.Seconds(c.Range.Start))/c.Speed);
    public static RationalTime Time(double seconds)=>new(checked((long)Math.Round(seconds*1000000)),1000000);
    private static ImmutableArray<MediaClip> Clips(EditorRecipe recipe)=>recipe switch {AudioRecipe a=>a.Clips,VideoRecipe v=>v.Clips,VisualizerRecipe v=>v.Audio.Clips,_=>throw new ArgumentException("Not media.")};
    private static ChannelMatrix IdentityMatrix(ImmutableArray<string> names)=>new(names,names,Enumerable.Range(0,names.Length*names.Length).Select(i=>i/names.Length==i%names.Length?1d:0d).ToImmutableArray(),false);
}
