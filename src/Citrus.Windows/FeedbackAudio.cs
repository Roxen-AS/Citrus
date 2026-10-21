namespace Citrus.Windows;

public enum FeedbackCue { Open, Selection, Success, Error }

/// <summary>Original quiet PCM click, mixed off the dispatcher. Never changes device volume.</summary>
public sealed class FeedbackAudio : IDisposable
{
    private readonly object gate = new();
    private readonly Task<IPlayer> loaded;
    private readonly TimeProvider clock;
    private long lastSelection = long.MinValue, epoch;
    private bool enabled = true, disposed;
    public FeedbackAudio() : this(Task.Run<IPlayer>(() =>
    {
        byte[] wave = CreateWave(FeedbackCue.Selection);
        short[] pcm = new short[(wave.Length - 44) / 2];
        Buffer.BlockCopy(wave, 44, pcm, 0, wave.Length - 44);
        return new WaveOutFeedbackPlayer(pcm, new WaveOutFeedbackOutput());
    }), TimeProvider.System) { }
    internal FeedbackAudio(Task<IPlayer> loaded, TimeProvider clock)
    {
        this.loaded = loaded;
        this.clock = clock;
    }
    public bool Enabled
    {
        get { lock (gate) return enabled; }
        set { lock (gate) { enabled = value; if (!value) Stop(); } }
    }
    public void Play(FeedbackCue cue)
    {
        lock (gate)
        {
            if (disposed || !enabled || cue != FeedbackCue.Selection) return;
            // A tick belongs to this selection only. Loading must never queue stale ticks.
            if (!loaded.IsCompletedSuccessfully) { _ = loaded.Exception; return; }
            try
            {
                loaded.Result.Play();
                lastSelection = clock.GetTimestamp();
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { /* Optional feedback cannot affect a drop. */ }
        }
    }
    public void Stop()
    {
        lock (gate)
        {
            epoch++;
            lastSelection = long.MinValue;
            try { if (loaded.IsCompletedSuccessfully) loaded.Result.Stop(); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { /* Optional device feedback. */ }
        }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; Stop(); }
        _ = loaded.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully) task.Result.Dispose();
            _ = task.Exception;
        }, TaskScheduler.Default);
    }
    internal interface IPlayer : IDisposable
    {
        void Play();
        void Stop();
    }
    private static byte[] CreateWave(FeedbackCue cue)
    {
        // Original short tactile click, matched to the verified reference's 60 ms / 48 kHz stereo format.
        // A decaying noise transient avoids the old sustained musical beep.
        const int rate = 48000, channels = 2, count = 2880;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + count * channels * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2);
        writer.Write((short)(channels * 2)); writer.Write((short)16); writer.Write("data"u8); writer.Write(count * channels * 2);
        var noise = new Random(7301);
        double previous = 0;
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)rate, sample = noise.NextDouble() * 2 - 1;
            double transient = sample - previous; previous = sample;
            double envelope = (1 - Math.Exp(-t * 2500)) * Math.Exp(-t * 160);
            short pcm = (short)(envelope * (transient * 1350 + Math.Sin(2 * Math.PI * 1700 * t) * 750));
            writer.Write(pcm); writer.Write(pcm);
        }
        return stream.ToArray();
    }
}
