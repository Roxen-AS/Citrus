using System.IO;
using System.Diagnostics;
using Citrus.Conversion.Editing.Media;
using Citrus.Core.Editing;
using Citrus.Windows.Editors.Media;
using Xunit.Abstractions;

namespace Citrus.Windows.Tests;

public sealed class NativeAudioFactAttribute : FactAttribute
{
    public NativeAudioFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CITRUS_RUN_AUDIO_DEVICE_TESTS") != "1")
            Skip = "Requires an actual Windows audio endpoint; set CITRUS_RUN_AUDIO_DEVICE_TESTS=1. Compilation/fake sinks are not native audio evidence.";
    }
}
public sealed class NativeMediaPlaybackTests(ITestOutputHelper output)
{
    [NativeAudioFact]
    public async Task Actual_wasapi_device_clock_cancel_release_and_reopen_or_record_no_endpoint()
    {
        string path = Path.Combine(Path.GetTempPath(), "Citrus-native-audio-" + Guid.NewGuid().ToString("N") + ".pcm");
        // Low-amplitude audible PCM, no system codecs or external input. Test is explicitly opt-in.
        byte[] samples = new byte[48000 * 2];
        for (int i = 0; i < 48000; i++) BitConverter.GetBytes((short)(200 * Math.Sin(i * 2 * Math.PI * 440 / 48000))).CopyTo(samples, i * 2);
        await File.WriteAllBytesAsync(path, samples);
        try
        {
            var pcm = new AudioPcmInfo(path, 48000, 1, 48000);
            await using var sink = new WasapiPreviewSink();
            using var cancel = new CancellationTokenSource();
            try { await sink.StartAsync(pcm, cancel.Token); }
            catch (AudioDeviceUnavailableException ex)
            {
                output.WriteLine("NATIVE AUDIO CAPABILITY: NO DEFAULT RENDER ENDPOINT. Output/clock proof remains pending. " + ex.Message);
                return;
            }
            var watch = Stopwatch.StartNew();
            var previous = sink.Position;
            while (sink.Position.CompareTo(new RationalTime(1, 10)) < 0)
            {
                await Task.Delay(10);
                Assert.True(sink.Position.CompareTo(previous) >= 0);
                previous = sink.Position;
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Actual WASAPI clock failed to advance.");
            }
            Assert.InRange((double)sink.Position.Numerator / sink.Position.Denominator, .1, .8);
            cancel.Cancel(); sink.Stop();
            Assert.True(sink.Completion.IsCompleted);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sink.Completion);
            // Reacquire a fresh endpoint after canceled worker termination and COM teardown.
            await using var reopened = new WasapiPreviewSink();
            await reopened.StartAsync(pcm, default);
            await Task.Delay(50); reopened.Stop();
            Assert.True(reopened.Completion.IsCompleted);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            output.WriteLine("NATIVE AUDIO EVIDENCE: actual shared-mode WASAPI PCM submission, advancing IAudioClock, cancel/Stop/Reset/release and fresh reopen succeeded. Physical audibility is not measured.");
        }
        finally { File.Delete(path); }
    }
}
