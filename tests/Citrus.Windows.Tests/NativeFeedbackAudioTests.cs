using System.Reflection;
using Xunit.Abstractions;

namespace Citrus.Windows.Tests;

public sealed class NativeFeedbackAudioTests(ITestOutputHelper output)
{
    [NativeAudioFact]
    public async Task Actual_waveout_endpoint_completes_buffers_and_reopens_after_rapid_clicks()
    {
        byte[] wave = (byte[])typeof(FeedbackAudio).GetMethod("CreateWave",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[FeedbackCue.Selection])!;
        short[] pcm = new short[(wave.Length-44)/2]; Buffer.BlockCopy(wave,44,pcm,0,wave.Length-44);
        using (var device = new WaveOutFeedbackOutput())
        {
            // Test real WAVEHDR completion before wrapping the endpoint in the mixer worker.
            device.Submit(0,pcm.Take(device.SamplesPerBuffer).ToArray());
            await WaitUntil(() => device.IsAvailable(0));
            var observed = new ObservedOutput(device);
            using var player = new WaveOutFeedbackPlayer(pcm,observed);
            for(int i=0;i<48;i++) { player.Play(); await Task.Delay(2); }
            await WaitUntil(() => observed.Submissions >= 12);
            await Task.Delay(100);
            await WaitUntil(() => Enumerable.Range(0,device.BufferCount).All(device.IsAvailable));
            Assert.False((bool)typeof(WaveOutFeedbackPlayer).GetField("failed",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(player)!);
            player.Stop();
            int beforeRestart = observed.Submissions;
            player.Play();
            // A fresh 60 ms click must submit twelve new 5 ms blocks, rather than pass while idle.
            await WaitUntil(() => observed.Submissions >= beforeRestart + 12);
            await WaitUntil(() => Enumerable.Range(0,device.BufferCount).All(device.IsAvailable));
            Assert.False((bool)typeof(WaveOutFeedbackPlayer).GetField("failed",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(player)!);
        }
        using (var reopened = new WaveOutFeedbackOutput())
        {
            reopened.Submit(0,pcm.Take(reopened.SamplesPerBuffer).ToArray());
            await WaitUntil(() => reopened.IsAvailable(0));
        }
        output.WriteLine("NATIVE FEEDBACK EVIDENCE: real waveOut PCM buffer completion, 48 rapid mixer requests, Stop/restart and endpoint reopen succeeded. Physical audibility/timbre is not measured.");
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while(!ready()) await Task.Delay(2,timeout.Token);
    }

    private sealed class ObservedOutput(WaveOutFeedbackOutput native) : WaveOutFeedbackPlayer.IOutput
    {
        private int submissions;
        public int Submissions => Volatile.Read(ref submissions);
        public int BufferCount => native.BufferCount;
        public int SamplesPerBuffer => native.SamplesPerBuffer;
        public ManualResetEvent BufferCompleted => native.BufferCompleted;
        public bool IsAvailable(int index) => native.IsAvailable(index);
        public void Submit(int index,short[] samples)
        {
            native.Submit(index,samples);
            Interlocked.Increment(ref submissions);
        }
        public void Reset() => native.Reset();
        public void Dispose() => native.Dispose();
    }
}
