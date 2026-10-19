using System.Collections.Immutable;
using System.Globalization;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Editing.Media;

/// <summary>Compiles prepared, verified media to raw preview output. Export/effect capabilities are added by the family renderer, not silently discarded here.</summary>
public sealed partial class MediaGraphCompiler(MediaProbe probe,MediaCapabilities capabilities)
{
    public MediaGraphPlan Compile(EditorDocument document,RenderPurpose purpose,PreviewRequest? preview,ImmutableDictionary<Guid,string> stagedSources)
    {
        var issues=EditorRecipeValidator.Validate(document);
        if(!issues.IsEmpty) throw new ArgumentException(string.Join("; ",issues.Select(i=>i.FieldPath+": "+i.Message)));
        if(document.Export.Compression is not QualityGoal) throw new NotSupportedException("Measured byte/reduction compression requires the media export renderer.");
        if(purpose!=RenderPurpose.Preview) throw new NotSupportedException("Media export encoding and compression are not available in the preview backend.");
        if(preview is null||preview.Bounds.Width is <1 or >2048||preview.Bounds.Height is <1 or >2048||preview.SourceTile is not null||preview.PageId is not null)
            throw new ArgumentException("Media preview requires bounds of 1..2048 pixels and no raster tile/page.");
        var clips=document.Recipe switch {VideoRecipe v=>v.Clips,AudioRecipe a=>a.Clips,_=>throw new NotSupportedException("This preview backend supports video frames and audio PCM only.")};
        var bindings=ImmutableArray.CreateBuilder<VerifiedMediaSource>();
        foreach(var source in document.Sources)
        {
            if(!stagedSources.TryGetValue(source.Id,out var path)) throw new ArgumentException("Missing staged source identity.");
            bindings.Add(probe.Bind(source,path));
        }
        var starts=new List<RationalTime>(); var duration=MediaTime.Zero;
        foreach(var clip in clips)
        {
            var info=probe.GetInfo(clip.SourceId);
            if(clip.Range.End.CompareTo(info.Duration)>0) throw new ArgumentException("Clip range exceeds the inspected source duration.");
            if(clip.VideoStream is {} v&&!info.Video.Any(s=>s.Index==v)||clip.AudioStream is {} a&&!info.Audio.Any(s=>s.Index==a)) throw new ArgumentException("Clip names an unknown stream.");
            starts.Add(duration); duration=MediaTime.Add(duration,MediaTime.Div(MediaTime.Sub(clip.Range.End,clip.Range.Start),MediaTime.Speed(clip.Speed)));
        }
        return document.Recipe switch
        {
            VideoRecipe video=>Video(document,preview,video,starts,duration,bindings.ToImmutable()),
            AudioRecipe audio=>Audio(document,preview,audio,duration,bindings.ToImmutable()),
            _=>throw new NotSupportedException()
        };
    }
    private MediaGraphPlan Video(EditorDocument document,PreviewRequest preview,VideoRecipe video,List<RationalTime> starts,RationalTime duration,ImmutableArray<VerifiedMediaSource> bindings)
    {
        if(!video.Masks.IsEmpty||!video.MetadataEdits.IsEmpty||!video.SplitAt.IsEmpty) throw new NotSupportedException("Timed masks, metadata editing and split exports require the video editor renderer.");
        Require(["trim","setpts","scale","setsar","format"],"rawvideo","rawvideo");
        var requested=preview.Time??video.SnapshotAt??MediaTime.Zero;
        if(requested.CompareTo(MediaTime.Zero)<0||requested.CompareTo(duration)>=0) throw new ArgumentOutOfRangeException(nameof(preview),"Scrub time must be inside the assembled timeline.");
        int selected=starts.FindLastIndex(s=>s.CompareTo(requested)<=0);
        var clip=video.Clips[selected]; var info=probe.GetInfo(clip.SourceId);
        var stream=info.Video.FirstOrDefault(s=>s.Index==clip.VideoStream)??throw new NotSupportedException("Selected clip has no video stream.");
        if(stream.IsHdr) throw new NotSupportedException("HDR visual editing requires a validated tone-map or HDR preservation pipeline.");
        var speed=MediaTime.Speed(clip.Speed);
        var sourceTime=MediaTime.Add(clip.Range.Start,MediaTime.Mul(MediaTime.Sub(requested,starts[selected]),speed));
        var stamp=probe.SelectFrame(clip.SourceId,stream.Index,clip.Range,sourceTime);
        var actual=MediaTime.Add(starts[selected],MediaTime.Div(MediaTime.Sub(stamp.SourceOffset,clip.Range.Start),speed));
        var args=new List<string>{"-copyts","-filter_threads","1","-filter_complex_threads","1","-noautorotate"};
        args.AddRange(MediaProcess.InputArguments(bindings.Single(b=>b.Snapshot.Id==clip.SourceId).Path));
        int sarWidth=checked((int)MediaTime.Ceiling(MediaTime.Mul(new(stream.CodedSize.Width,1),stream.SampleAspect),1));
        var filters=new List<string>{ $"scale={sarWidth}:{stream.CodedSize.Height}","setsar=1" };
        switch(stream.RotationDegrees)
        {
            case 90: RequireFilters("transpose"); filters.Add("transpose=cclock"); break;
            case 180: RequireFilters("hflip","vflip"); filters.Add("hflip,vflip"); break;
            case 270: RequireFilters("transpose"); filters.Add("transpose=clock"); break;
            case 0: break;
            default: throw new NotSupportedException("Only quarter-turn video rotation is available.");
        }
        // Trim in signed native stream ticks with copyts. Never seek by guessed FPS or keyframe index.
        filters.Add($"trim=start_pts={Number(stamp.RawPts)}:end_pts={Number(checked(stamp.RawPts+1))}");
        filters.Add("setpts=PTS-STARTPTS");
        var firstClip=video.Clips[0];
        var firstStream=probe.GetInfo(firstClip.SourceId).Video.FirstOrDefault(s=>s.Index==firstClip.VideoStream)
            ??throw new NotSupportedException("The first clip requires a video stream to define the common display plane.");
        int cw=firstStream.DisplaySize.Width,ch=firstStream.DisplaySize.Height;
        MediaProbe.ValidateSize(video.Canvas);
        switch(video.Fit)
        {
            case ImageFit.Stretch: filters.Add($"scale={cw}:{ch}"); break;
            case ImageFit.Contain:
                RequireFilters("pad"); filters.Add($"scale={cw}:{ch}:force_original_aspect_ratio=decrease,pad={cw}:{ch}:(ow-iw)/2:(oh-ih)/2:color=black"); break;
            case ImageFit.Cover:
                RequireFilters("crop"); filters.Add($"scale={cw}:{ch}:force_original_aspect_ratio=increase,crop={cw}:{ch}:exact=1"); break;
            default: throw new NotSupportedException("Unknown canvas fit.");
        }
        if(video.Crop is {} crop)
        {
            if(new[]{crop.X,crop.Y,crop.Width,crop.Height}.Any(v=>v!=Math.Truncate(v))) throw new NotSupportedException("Video crop must use integral display-pixel edges.");
            if(crop.X+crop.Width>cw||crop.Y+crop.Height>ch) throw new ArgumentException("Crop exceeds the first clip's normalized display plane.");
            RequireFilters("crop"); int width=checked((int)crop.Width),height=checked((int)crop.Height);
            filters.Add($"crop={width}:{height}:{checked((int)crop.X)}:{checked((int)crop.Y)}:exact=1");
        }
        // Canvas is final output size. Preview downsampling is last, after crop in source display coordinates.
        decimal scale=Math.Min(1m,Math.Min((decimal)preview.Bounds.Width/video.Canvas.Width,(decimal)preview.Bounds.Height/video.Canvas.Height));
        var size=new SizeI(Math.Max(1,(int)(video.Canvas.Width*scale)),Math.Max(1,(int)(video.Canvas.Height*scale)));
        filters.Add($"scale={size.Width}:{size.Height},setsar=1,format=bgra");
        args.AddRange(["-map",$"0:{stream.Index}","-vf",string.Join(',',filters),"-an","-sn","-dn","-frames:v","1","-fps_mode","passthrough","-c:v","rawvideo","-pix_fmt","bgra","-f","rawvideo","pipe:1"]);
        return new(document.Revision,preview.Generation,actual,new(clip.SourceId,stream.Index,stamp.RawPts),[..args],size,null,null,duration,bindings,probe);
    }
    private MediaGraphPlan Audio(EditorDocument document,PreviewRequest preview,AudioRecipe audio,RationalTime duration,ImmutableArray<VerifiedMediaSource> bindings)
    {
        if(!audio.Bleeps.IsEmpty||audio.Loudness is not null||!audio.MetadataEdits.IsEmpty) throw new NotSupportedException("Bleep, loudness and metadata processing require the audio editor renderer.");
        int channels=audio.Channels.OutputChannels.Length;
        if(audio.Channels.LimitPeak||!audio.Channels.InputChannels.SequenceEqual(audio.Channels.OutputChannels)||audio.Channels.Coefficients.Where((value,index)=>value!=(index/channels==index%channels?1:0)).Any())
            throw new NotSupportedException("Only an explicit identity channel matrix is available in this preview backend.");
        Require(["asettb","atrim","asetpts","aresample","atempo","apad","concat"],"pcm_s16le","s16le");
        var args=new List<string>{"-copyts","-filter_threads","1","-filter_complex_threads","1"};
        var graph=new List<string>(); long totalSampleFrames=0;
        for(int i=0;i<audio.Clips.Length;i++)
        {
            var clip=audio.Clips[i]; var source=probe.GetInfo(clip.SourceId);
            var stream=source.Audio.FirstOrDefault(s=>s.Index==clip.AudioStream)??throw new NotSupportedException("Selected clip has no audio stream.");
            if(!stream.Channels.SequenceEqual(audio.Channels.InputChannels)) throw new NotSupportedException("Selected stream channel layout does not match the identity matrix.");
            args.AddRange(MediaProcess.InputArguments(bindings.Single(b=>b.Snapshot.Id==clip.SourceId).Path));
            var start=MediaTime.Add(source.PresentationOrigin,clip.Range.Start);
            var end=MediaTime.Add(source.PresentationOrigin,clip.Range.End);
            // Decoded audio can use a different time base from its container (e.g. Matroska 1/1000).
            // Establish sample ticks explicitly before trimming; all boundaries stay exact rationals.
            var filterTimeBase=new RationalTime(1,stream.SampleRate);
            long startPts=MediaTime.CeilingTicks(start,filterTimeBase),endPts=MediaTime.CeilingTicks(end,filterTimeBase);
            var length=MediaTime.Div(MediaTime.Sub(clip.Range.End,clip.Range.Start),MediaTime.Speed(clip.Speed));
            long sampleFrames=MediaTime.Ceiling(length,audio.SampleRate);
            totalSampleFrames=checked(totalSampleFrames+sampleFrames);
            var chain=$"[{i}:{stream.Index}]asettb=1/{stream.SampleRate},atrim=start_pts={Number(startPts)}:end_pts={Number(endPts)},asetpts=PTS-({Number(startPts)}),aresample={audio.SampleRate}:first_pts=0";
            double tempo=clip.Speed;
            while(tempo>2) { chain+=",atempo=2"; tempo/=2; }
            while(tempo<.5) { chain+=",atempo=0.5"; tempo/=.5; }
            if(tempo!=1) chain+=",atempo="+tempo.ToString("0.######",CultureInfo.InvariantCulture);
            chain+=$",apad=whole_len={Number(sampleFrames)},atrim=end_sample={Number(sampleFrames)},asetpts=N/SR/TB[a{i}]";
            graph.Add(chain);
        }
        graph.Add(string.Concat(Enumerable.Range(0,audio.Clips.Length).Select(i=>$"[a{i}]"))+$"concat=n={audio.Clips.Length}:v=0:a=1[out]");
        args.AddRange(["-filter_complex",string.Join(';',graph),"-map","[out]","-vn","-sn","-dn","-ar",Number(audio.SampleRate),"-ac",Number(channels),"-c:a","pcm_s16le","-f","s16le","pipe:1"]);
        return new(document.Revision,preview.Generation,null,null,[..args],null,audio.SampleRate,channels,duration,bindings,probe,totalSampleFrames);
    }
    private void Require(string[] filters,string encoder,string muxer)
    {
        RequireFilters(filters);
        if(!capabilities.Encoders.Contains(encoder)||!capabilities.Muxers.Contains(muxer)) throw new NotSupportedException($"Bundled raw preview requires {encoder}/{muxer}.");
    }
    private void RequireFilters(params string[] filters) { foreach(string filter in filters) if(!capabilities.Filters.Contains(filter)) throw new NotSupportedException("Bundled media filter is unavailable: "+filter); }
    private static string Number(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
