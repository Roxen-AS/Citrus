using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;
using Xunit.Abstractions;

namespace Citrus.Conversion.Tests;

public sealed class MediaPreviewSeekTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Near_end_vfr_poster_decodes_from_a_nearby_keyframe_with_exact_pixels_and_identity()
    {
        string root=Path.Combine(Path.GetTempPath(),"citrus-exact-seek-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var process=new MediaProcess(Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG")??"/usr/bin/ffmpeg",Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE")??"/usr/bin/ffprobe");
            string raw=Path.Combine(root,"frames.bgra"),encoded=Path.Combine(root,"source.mp4"),input=Path.Combine(root,"rotated.mp4");
            using(var file=File.Create(raw))for(int frame=0;frame<4800;frame++)for(int y=0;y<8;y++)for(int x=0;x<16;x++)
            {
                byte color=(byte)(20+(frame%4)*15+(y<4?(x<8?0:35):(x<8?80:125)));
                file.Write([color,color,color,255]);
            }
            await process.RunAsync(["-f","rawvideo","-pixel_format","bgra","-video_size","16x8","-framerate","50","-i",raw,
                "-vf","settb=1/90000,setpts=900000+N*1800+floor(N/3)*901,setsar=3/2","-fps_mode","vfr","-enc_time_base","1:30000","-video_track_timescale","90000","-g","48","-bf","2","-c:v","mpeg4","-q:v","1",encoded],null,default);
            await process.RunAsync(["-copyts","-display_rotation","90","-i",encoded,"-c","copy",input],null,default);
            var snapshots=new SourceSnapshotService();var captured=(await snapshots.CaptureAsync([input],default))[0].Snapshot!;
            await using var probe=new MediaProbe(process,snapshots,Path.Combine(root,"private"));
            var info=await probe.InspectAsync(captured,default);var stream=Assert.Single(info.Video);
            Assert.Equal(new RationalTime(10,1),info.PresentationOrigin);
            Assert.Equal(new RationalTime(3,2),stream.SampleAspect);
            Assert.Equal(new SizeI(8,24),stream.DisplaySize);
            using var independent=await process.ProbeJsonAsync(["-show_frames","-show_entries","frame=pts:frame_side_data=","-select_streams","v:0","-of","json","-i",input],1024*1024,default);
            var pts=independent.RootElement.GetProperty("frames").EnumerateArray().Select(f=>f.GetProperty("pts").GetInt64()).ToArray();
            int selected=Enumerable.Range(pts.Length-100,90).Last(i=>(pts[i]*stream.TimeBase.Numerator*1_000_000)%stream.TimeBase.Denominator!=0);
            var requested=new RationalTime((pts[selected]-pts[0])*stream.TimeBase.Numerator,stream.TimeBase.Denominator);
            var clip=new MediaClip(Guid.NewGuid(),captured.Id,stream.Index,null,new(new(0,1),info.Duration),1);
            var document=new EditorDocument(1,1,[captured],[],new VideoRecipe([clip],null,stream.DisplaySize,ImageFit.Contain,true,[],[],null,[]),new("mp4",null,"copy",new QualityGoal(85),MetadataPolicy.StripAll,new()));
            var plan=await new MediaGraphCompiler(probe,await process.DiscoverCapabilitiesAsync(default)).CompilePosterAsync(document,new(requested,info.Duration),false,stream.DisplaySize,1,default);
            Assert.Equal(pts[selected],plan.Graph.FrameIdentity!.RawPts);
            Assert.Equal(requested,plan.Graph.FrameTime);
            var (actual,decoded)=await DecodeWithCountAsync(process.FfmpegPath,plan.Graph.Arguments);
            var (expected,fromStart)=await DecodeWithCountAsync(process.FfmpegPath,["-copyts","-noautorotate","-i",input,"-vf",$"trim=start_pts={pts[selected]}:end_pts={pts[selected]+1},scale=24:8,setsar=1,transpose=cclock,format=bgra","-an","-sn","-dn","-frames:v","1","-fps_mode","passthrough","-c:v","rawvideo","-pix_fmt","bgra","-f","rawvideo","pipe:1"]);
            Assert.Equal(expected,actual);
            output.WriteLine($"Exact poster at frame {selected}/{pts.Length}: {decoded} frames decoded; independent decode from start: {fromStart}.");
            Assert.InRange(decoded,1,100);
            Assert.True(fromStart>=selected&&decoded*10<fromStart);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    private static async Task<(byte[] Pixels,int DecodedFrames)> DecodeWithCountAsync(string executable,ImmutableArray<string> arguments)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in new[]{"-hide_banner","-nostdin","-nostats","-loglevel","verbose","-protocol_whitelist","file,pipe"}.Concat(arguments))start.ArgumentList.Add(argument);
        using var child=Process.Start(start)!;
        using var registration=timeout.Token.Register(()=>{try{child.Kill(true);}catch(InvalidOperationException){}});
        using var output=new MemoryStream();
        var errors=child.StandardError.ReadToEndAsync(timeout.Token);
        await Task.WhenAll(child.StandardOutput.BaseStream.CopyToAsync(output,timeout.Token),child.WaitForExitAsync(timeout.Token));
        string diagnostics=await errors;
        Assert.True(child.ExitCode==0,diagnostics);
        var counts=Regex.Matches(diagnostics,@"(\d+) frames decoded;");
        Assert.Single(counts.Cast<Match>());
        return(output.ToArray(),int.Parse(counts[0].Groups[1].Value,CultureInfo.InvariantCulture));
    }
}
