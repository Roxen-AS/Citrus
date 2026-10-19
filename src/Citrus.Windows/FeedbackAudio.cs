using System.Media;

namespace Citrus.Windows;

public enum FeedbackCue { Open, Selection, Success, Error }

/// <summary>Original quiet PCM tones, loaded once off the dispatcher. Never changes device volume.</summary>
public sealed class FeedbackAudio : IDisposable
{
    private readonly object gate = new();
    private readonly Task<Dictionary<FeedbackCue, SoundPlayer>> loaded;
    private long lastSelection = long.MinValue, epoch;
    private bool enabled = true, disposed;
    public FeedbackAudio() => loaded = Task.Run(() => Enum.GetValues<FeedbackCue>().ToDictionary(cue => cue, cue =>
    {
        var player = new SoundPlayer(new MemoryStream(CreateWave(cue)));
        player.Load();
        return player;
    }));
    public bool Enabled
    {
        get { lock (gate) return enabled; }
        set { lock (gate) { enabled = value; if (!value) Stop(); } }
    }
    public void Play(FeedbackCue cue)
    {
        long request;
        lock (gate)
        {
            if (disposed || !enabled) return;
            long now = Environment.TickCount64;
            if (cue == FeedbackCue.Selection && lastSelection != long.MinValue && now - lastSelection < 45) return;
            if (cue == FeedbackCue.Selection) lastSelection = now;
            request = epoch;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var players = await loaded.ConfigureAwait(false);
                lock (gate) if (!disposed && enabled && epoch == request) players[cue].Play();
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { /* Optional feedback cannot affect a drop. */ }
        });
    }
    public void Stop()
    {
        lock (gate)
        {
            epoch++;
            if (loaded.IsCompletedSuccessfully) foreach (var player in loaded.Result.Values) player.Stop();
        }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; Stop(); }
        _ = loaded.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully) foreach (var player in task.Result.Values) { player.Stream?.Dispose(); player.Dispose(); }
            _ = task.Exception;
        }, TaskScheduler.Default);
    }
    private static byte[] CreateWave(FeedbackCue cue)
    {
        const int rate = 22050;
        int count = (int)(rate * (cue == FeedbackCue.Selection ? .035 : .095));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate*2);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(count*2);
        double frequency = cue switch { FeedbackCue.Open => 660, FeedbackCue.Selection => 880, FeedbackCue.Success => 1046.5, _ => 330 };
        for (int i=0;i<count;i++)
        {
            double t = i/(double)count;
            double envelope = Math.Sin(Math.PI*t) * Math.Exp(-3*t);
            writer.Write((short)(1500 * envelope * Math.Sin(2*Math.PI*frequency*i/rate)));
        }
        return stream.ToArray();
    }
}
