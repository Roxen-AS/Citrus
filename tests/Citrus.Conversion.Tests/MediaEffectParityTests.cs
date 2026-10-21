using System.Collections.Immutable;
using Citrus.Conversion.Editing;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;

namespace Citrus.Conversion.Tests;

public sealed class MediaEffectParityTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "citrus-media-effects-" + Guid.NewGuid().ToString("N"));
    private MediaProcess process = null!;
    private MediaProbe probe = null!;
    private SourceSnapshot source = null!;
    private MediaCapabilities capabilities = null!;
    private EditorDocument document = null!;
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        process = new(Environment.GetEnvironmentVariable("CITRUS_TEST_FFMPEG") ?? "/usr/bin/ffmpeg", Environment.GetEnvironmentVariable("CITRUS_TEST_FFPROBE") ?? "/usr/bin/ffprobe");
        string input = Path.Combine(directory, "tone.wav");
        MediaFixture.WriteWave(input, Enumerable.Repeat((short)8000,48000).ToArray());
        var sources = new SourceSnapshotService();
        source = (await sources.CaptureAsync([input],default)).Single().Snapshot!;
        probe = new(process,sources,Path.Combine(directory,"probe"));
        var info = await probe.InspectAsync(source,default);
        capabilities = await process.DiscoverCapabilitiesAsync(default);
        var clip = new MediaClip(Guid.NewGuid(),source.Id,null,info.Audio[0].Index,new(new(0,1),new(1,1)),1);
        document = new(1,0,[source],[],new AudioRecipe([clip],48000,new(["FC"],["FC"],[1],false),[],null,[]),new("wav",directory,"result",new QualityGoal(85),MetadataPolicy.StripAll,new()));
    }
    public async Task DisposeAsync() { await probe.DisposeAsync(); Directory.Delete(directory,true); }

    [Fact]
    public async Task Channel_matrix_changes_actual_preview_samples()
    {
        var audio = (AudioRecipe)document.Recipe;
        var edited = document with { Recipe=audio with { Channels=new(["FC"],["FL","FR"],[.5,0],false) } };
        var samples = await Decode(edited,"channels.pcm");
        Assert.InRange(BitConverter.ToInt16(samples,10000*4),3990,4010);
        Assert.Equal(0,BitConverter.ToInt16(samples,10000*4+2));
        Assert.Equal(48000*4,samples.Length);
    }

    [Fact]
    public async Task Silence_bleep_removes_original_only_inside_selected_range()
    {
        var audio = (AudioRecipe)document.Recipe;
        var region = new BleepRegion(Guid.NewGuid(),new(new(1,4),new(3,4)),BleepMode.Silence,1000,-12,0,[0]);
        var samples = await Decode(document with { Recipe=audio with { Bleeps=[region] } },"bleep.pcm");
        Assert.InRange(BitConverter.ToInt16(samples,1000*2),7990,8010);
        Assert.Equal(0,BitConverter.ToInt16(samples,24000*2));
        Assert.InRange(BitConverter.ToInt16(samples,47000*2),7990,8010);
        Assert.Equal(96000,samples.Length);
    }

    [Fact]
    public async Task Tone_bleep_replaces_instead_of_mixing_original()
    {
        var audio = (AudioRecipe)document.Recipe;
        var region = new BleepRegion(Guid.NewGuid(),new(new(1,4),new(3,4)),BleepMode.Tone,1000,-12,5,[0]);
        var samples = await Decode(document with { Recipe=audio with { Bleeps=[region] } },"tone.pcm");
        double mean = Enumerable.Range(16000,12000).Average(i=>(double)BitConverter.ToInt16(samples,i*2));
        Assert.InRange(mean,-20,20);
    }
    private async Task<byte[]> Decode(EditorDocument value,string name)
    {
        var graph = new MediaGraphCompiler(probe,capabilities).Compile(value,RenderPurpose.Preview,new(1,new(32,32),null,null,null),probe.StagedSources);
        var pcm = await new MediaBinaryDecoder(process).DecodeAudioAsync(graph,Path.Combine(directory,name),default);
        return await File.ReadAllBytesAsync(pcm.Path);
    }
}
