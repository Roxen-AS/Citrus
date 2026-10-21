namespace Citrus.Windows;

/// <summary>One pre-opened endpoint and one render worker for every selection click.</summary>
internal sealed class WaveOutFeedbackPlayer : FeedbackAudio.IPlayer
{
    private readonly object gate = new();
    private readonly FeedbackClickMixer mixer;
    private readonly IOutput output;
    private readonly AutoResetEvent requested = new(false);
    private readonly Thread worker;
    private readonly short[] block;
    private readonly WaitHandle[] signals;
    private int nextBuffer;
    private bool disposed, failed;

    public WaveOutFeedbackPlayer(short[] pcm, IOutput output)
    {
        this.output = output;
        try
        {
            mixer = new FeedbackClickMixer(pcm);
            if (output.BufferCount <= 0 || output.SamplesPerBuffer <= 0 || output.SamplesPerBuffer % 2 != 0)
                throw new ArgumentException("Stereo render buffers are required.", nameof(output));
            block = new short[output.SamplesPerBuffer];
            signals = [requested, output.BufferCompleted];
            worker = new Thread(Render) { IsBackground = true, Name = "Citrus feedback audio" };
            worker.Start();
        }
        catch { requested.Dispose(); output.Dispose(); throw; }
    }

    public void Play()
    {
        lock (gate)
        {
            if (disposed || failed || !mixer.TryStart()) return;
            requested.Set();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (disposed) return;
            Reset();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Reset();
            requested.Set();
        }
        // No callbacks into WPF or the owner; joining cannot wait on the dispatcher.
        worker.Join();
        output.Dispose();
        requested.Dispose();
    }

    private void Reset()
    {
        mixer.Clear(); nextBuffer = 0;
        try { output.Reset(); }
        catch (Exception) { failed = true; /* Device feedback is optional. */ }
    }

    private void Render()
    {
        try
        {
            while (true)
            {
                WaitHandle.WaitAny(signals);
                lock (gate)
                {
                    if (disposed || failed) return;
                    output.BufferCompleted.Reset();
                    // At most three 5 ms blocks may be in flight. A click joins the next
                    // available block rather than waiting behind a queue of whole clips.
                    for (int i = 0; i < output.BufferCount && mixer.HasVoices && output.IsAvailable(nextBuffer); i++)
                    {
                        mixer.Render(block);
                        output.Submit(nextBuffer, block);
                        nextBuffer = (nextBuffer + 1) % output.BufferCount;
                    }
                }
            }
        }
        catch (Exception)
        {
            lock (gate) { failed = true; Reset(); }
        }
    }

    internal interface IOutput : IDisposable
    {
        int BufferCount { get; }
        int SamplesPerBuffer { get; }
        ManualResetEvent BufferCompleted { get; }
        bool IsAvailable(int index);
        void Submit(int index, short[] samples);
        void Reset();
    }
}
