using System.IO;
using Citrus.Conversion;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Media;
using Citrus.Core;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Media;

namespace Citrus.Windows.Tests;

/// <summary>Real decoder/source lifecycle proof; no display or audio endpoint is required.</summary>
public sealed class MediaPosterIntegrationTests
{
    [Fact]
    public async Task Known_engine_capabilities_prepare_a_poster_without_rediscovering_the_media_executable()
    {
        string root=Path.Combine(Path.GetTempPath(),"citrus-poster-capabilities-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var process=MediaProcess.Bundled();
            string raw=Path.Combine(root,"frames.bgra"),input=Path.Combine(root,"source.mp4");
            await File.WriteAllBytesAsync(raw,new byte[16*8*4*3]);
            await process.RunAsync(["-f","rawvideo","-pixel_format","bgra","-video_size","16x8","-framerate","8","-i",raw,"-c:v","mpeg4",input],null,default);
            var snapshots=new SourceSnapshotService();var source=(await snapshots.CaptureAsync([input],default))[0].Snapshot!;
            await using var probe=new MediaProbe(process,snapshots,Path.Combine(root,"shared-sources"));
            var info=await probe.InspectAsync(source,default);
            var known=await process.DiscoverCapabilitiesAsync(default);
            // Preparing metadata cannot invoke FFmpeg here; decoding is a later explicit step.
            var unavailable=new MediaProcess(Path.Combine(root,"must-not-rediscover.exe"),process.FfprobePath);
            await using var preparation=new MediaPlaybackPreparation(unavailable,snapshots,Path.Combine(root,"preview"),new(16,8),probe,known);
            var document=new EditorDocument(1,1,[source],[],new VideoRecipe([new(Guid.NewGuid(),source.Id,info.Video[0].Index,null,new(new(0,1),info.Duration),1)],null,new(16,8),ImageFit.Contain,false,[],[],null,[]),new("mp4",null,"copy",new QualityGoal(85),MetadataPolicy.StripAll,new()));
            await using var ready=await preparation.PreparePausedAsync(document,new(new(0,1),info.Duration),false,1,default);
            Assert.True(ready.HasVideo);
            Assert.Null(ready.Audio);
            Assert.Empty(ready.Waveform);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_frame_steps_cross_initial_and_refilled_vfr_windows(bool backwards)
    {
        string root=Path.Combine(Path.GetTempPath(),"citrus-poster-vfr-step-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var process=MediaProcess.Bundled();
            string raw=Path.Combine(root,"frames.bgra"),input=Path.Combine(root,"source.mp4");
            using(var file=File.Create(raw))for(int frame=0;frame<4800;frame++)for(int pixel=0;pixel<16*8;pixel++)file.Write([(byte)(frame%255),40,180,255]);
            await process.RunAsync(["-f","rawvideo","-pixel_format","bgra","-video_size","16x8","-framerate","50","-i",raw,
                "-vf","settb=1/90000,setpts=900000+N*1800+floor(N/3)*900","-fps_mode","vfr","-enc_time_base","1:30000","-video_track_timescale","90000","-c:v","mpeg4",input],null,default);
            var snapshots=new SourceSnapshotService();var source=(await snapshots.CaptureAsync([input],default))[0].Snapshot!;
            await using var probe=new MediaProbe(process,snapshots,Path.Combine(root,"shared-sources"));
            var info=await probe.InspectAsync(source,default);var stream=Assert.Single(info.Video);
            using var independent=await process.ProbeJsonAsync(["-show_frames","-show_entries","frame=pts","-select_streams","v:0","-of","json","-i",input],1024*1024,default);
            var pts=independent.RootElement.GetProperty("frames").EnumerateArray().Select(frame=>frame.GetProperty("pts").GetInt64()).ToArray();
            RationalTime At(int index)=>new((pts[index]-pts[0])*stream.TimeBase.Numerator,stream.TimeBase.Denominator);
            var document=new EditorDocument(1,1,[source],[],new VideoRecipe([new(Guid.NewGuid(),source.Id,stream.Index,null,new(new(0,1),info.Duration),1)],null,new(16,8),ImageFit.Contain,false,[],[],null,[]),new("mp4",null,"copy",new QualityGoal(85),MetadataPolicy.StripAll,new()));
            await using var controller=new MediaPreviewController(new MediaPlaybackPreparation(process,snapshots,Path.Combine(root,"preview"),new(16,8),probe),()=>throw new Exception("Paused frame stepping opened audio."));
            await controller.PreparePausedAsync(document,new(new(0,1),info.Duration),false,default);
            if(backwards)
            {
                await controller.SeekAsync(At(4350),default);
                for(int previous=4349;previous>=4347;previous--)
                {
                    await controller.StepAsync(-1,default);
                    Assert.Equal(At(previous),controller.Position);
                    Assert.True(controller.TryPresentFrame(frame=>Assert.Equal(pts[previous],frame.FrameIdentity!.RawPts)));
                }
            }
            else foreach(int initial in new[]{4094,4350})
            {
                await controller.SeekAsync(At(initial),default);
                for(int next=initial+1;next<=initial+3;next++)
                {
                    await controller.StepAsync(1,default);
                    Assert.Equal(At(next),controller.Position);
                    Assert.True(controller.TryPresentFrame(frame=>Assert.Equal(pts[next],frame.FrameIdentity!.RawPts)));
                }
            }
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Paused_video_reuses_private_source_and_never_creates_pcm_then_rejects_changed_original()
    {
        string root=Path.Combine(Path.GetTempPath(),"citrus-poster-proof-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var process=MediaProcess.Bundled();
            string raw=Path.Combine(root,"frames.bgra"),input=Path.Combine(root,"source.mp4");
            using(var file=File.Create(raw))for(int frame=0;frame<8;frame++)for(int pixel=0;pixel<16*8;pixel++)file.Write([(byte)(20+frame*25),40,180,255]);
            await process.RunAsync(["-f","rawvideo","-pixel_format","bgra","-video_size","16x8","-framerate","8","-i",raw,"-c:v","mpeg4",input],null,default);
            var snapshots=new SourceSnapshotService();
            var source=(await snapshots.CaptureAsync([input],default))[0].Snapshot!;
            await using var probe=new MediaProbe(process,snapshots,Path.Combine(root,"shared-sources"));
            var info=await probe.InspectAsync(source,default);
            string staged=probe.StagedSources[source.Id];
            var document=new EditorDocument(1,1,[source],[],new VideoRecipe([new(Guid.NewGuid(),source.Id,info.Video[0].Index,null,new(new(0,1),info.Duration),1)],null,new(16,8),ImageFit.Contain,false,[],[],null,[]),new("mp4",null,"copy",new QualityGoal(85),MetadataPolicy.StripAll,new()));
            var preparation=new MediaPlaybackPreparation(process,snapshots,Path.Combine(root,"preview"),new(16,8),probe);
            var controller=new MediaPreviewController(preparation,()=>throw new Exception("A paused video opened audio."));
            try
            {
                await controller.PreparePausedAsync(document,new(new(0,1),info.Duration),false,default);
                Assert.True(controller.TryPresentFrame(frame=>Assert.Equal(source.Id,frame.FrameIdentity!.SourceId)));
                await controller.SeekAsync(new(3,8),default);
                Assert.True(controller.TryPresentFrame(frame=>Assert.Equal(new RationalTime(3,8),frame.ActualTime)));
                Assert.Equal(staged,probe.StagedSources[source.Id]);
                Assert.Empty(Directory.EnumerateFiles(root,"*.pcm",SearchOption.AllDirectories));
                await File.AppendAllTextAsync(input,"changed");
                await Assert.ThrowsAsync<IOException>(()=>controller.SeekAsync(new(1,2),default));
            }
            finally{await controller.DisposeAsync();}
            Assert.True(File.Exists(staged)); // The externally supplied source session stays owned by the host.
            await probe.DisposeAsync();
            Assert.False(File.Exists(staged));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
