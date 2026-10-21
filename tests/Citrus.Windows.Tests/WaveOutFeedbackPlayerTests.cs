using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Citrus.Windows.Tests;

public sealed class WaveOutFeedbackPlayerTests
{
    [Fact]
    public async Task A_selection_during_playback_mixes_into_the_next_block_without_restarting_the_tail()
    {
        using var output = new ControlledOutput();
        using var player = new WaveOutFeedbackPlayer([100, 100, 200, 200, 300, 300, 400, 400, 500, 500, 600, 600, 700, 700, 800, 800], output);
        player.Play();
        await WaitUntil(() => output.History.Count == 3);
        player.Play();
        output.CompleteBuffers();
        await WaitUntil(() => output.History.Count >= 4);
        Assert.Equal<short>([800, 800, 1000, 1000], output.History.ElementAt(3));
    }

    [Fact]
    public async Task Output_keeps_a_bounded_ring_and_stop_discards_submitted_and_unrendered_tails()
    {
        using var output = new ControlledOutput();
        using var player = new WaveOutFeedbackPlayer(Enumerable.Repeat((short)100, 48).ToArray(), output);
        player.Play();
        await WaitUntil(() => output.History.Count == 3);
        Assert.All(output.History, block => Assert.Equal<short>([100, 100, 100, 100], block));
        Assert.Equal(3, output.PendingCount);

        player.Stop();
        Assert.Equal(0, output.PendingCount);
        output.BufferCompleted.Set(); // Late completion from the stopped generation.
        player.Play();
        await WaitUntil(() => output.History.Count == 6);
        Assert.All(output.History, block => Assert.Equal<short>([100, 100, 100, 100], block));
        Assert.Equal(3, output.PendingCount);
    }

    [Fact]
    public async Task Dispose_joins_the_owned_worker_and_releases_output_once()
    {
        using var output = new ControlledOutput();
        var player = new WaveOutFeedbackPlayer(Enumerable.Repeat((short)100, 48).ToArray(), output);
        player.Play();
        await WaitUntil(() => output.PendingCount == 3);
        player.Dispose(); player.Dispose(); player.Play();
        Assert.Equal(1, output.Disposals);
        Assert.Equal(0, output.PendingCount);
        Assert.Equal(3, output.History.Count);
    }

    [Fact]
    public async Task A_render_device_failure_clears_voices_and_cannot_replay_on_later_selections()
    {
        using var output = new ControlledOutput { FailWrite = true };
        using var player = new WaveOutFeedbackPlayer([100, 100, 200, 200], output);
        player.Play();
        await WaitUntil(() => output.Resets > 0);
        output.FailWrite = false;
        player.Play();
        Assert.Empty(output.History);
        Assert.Equal(0, output.PendingCount);
    }

    [Fact]
    public void Native_preparation_failure_resets_unprepares_and_closes_partially_opened_output()
    {
        var api = new RecordingWaveApi { FailPreparation = 2 };
        Assert.Throws<InvalidOperationException>(() => new WaveOutFeedbackOutput(api));
        Assert.Equal(new[] { "open", "prepare", "prepare-failed", "reset", "unprepare", "close" }, api.Calls);
    }

    [Fact]
    public void Native_reset_precedes_unprepare_and_close_for_all_submitted_blocks()
    {
        var api = new RecordingWaveApi();
        var output = new WaveOutFeedbackOutput(api);
        output.Submit(0, new short[output.SamplesPerBuffer]);
        Assert.False(output.IsAvailable(0));
        output.Dispose(); output.Dispose();
        Assert.Equal(new[] { "open", "prepare", "prepare", "prepare", "write", "reset", "unprepare", "unprepare", "unprepare", "close" }, api.Calls);
    }

    [Fact]
    public void Missing_native_endpoint_does_not_allocate_or_prepare_playback_buffers()
    {
        var api = new RecordingWaveApi { FailOpen = true };
        Assert.Throws<InvalidOperationException>(() => new WaveOutFeedbackOutput(api));
        Assert.Equal(new[] { "open-failed" }, api.Calls);
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready()) await Task.Delay(1, timeout.Token);
    }

    private sealed class ControlledOutput : WaveOutFeedbackPlayer.IOutput
    {
        private readonly object gate = new();
        private readonly bool[] pending = new bool[3];
        public ManualResetEvent BufferCompleted { get; } = new(false);
        public ConcurrentQueue<short[]> History { get; } = new();
        public int SamplesPerBuffer => 4;
        public int BufferCount => 3;
        public int PendingCount { get { lock (gate) return pending.Count(value => value); } }
        public int Disposals { get; private set; }
        public int Resets { get; private set; }
        public bool FailWrite { get; set; }
        public bool IsAvailable(int index) { lock (gate) return !pending[index]; }
        public void CompleteBuffers() { lock (gate) Array.Clear(pending); BufferCompleted.Set(); }
        public void Submit(int index, short[] samples)
        {
            lock (gate)
            {
                if (FailWrite) throw new InvalidOperationException("Endpoint removed");
                Assert.False(pending[index]);
                pending[index] = true;
                History.Enqueue((short[])samples.Clone());
            }
        }
        public void Reset() { lock (gate) { Array.Clear(pending); Resets++; } }
        public void Dispose()
        {
            lock (gate)
            {
                if (Disposals != 0) return;
                Disposals++; Array.Clear(pending); BufferCompleted.Dispose();
            }
        }
    }

    private sealed class RecordingWaveApi : WaveOutFeedbackOutput.IApi
    {
        private readonly List<IntPtr> prepared = [];
        public List<string> Calls { get; } = [];
        public int FailPreparation { get; init; }
        public bool FailOpen { get; init; }
        public uint Open(out IntPtr handle, ref WaveOutFeedbackOutput.WaveFormat format, IntPtr signal)
        {
            Calls.Add(FailOpen ? "open-failed" : "open");
            handle = FailOpen ? IntPtr.Zero : new IntPtr(1);
            Assert.Equal(48000u, format.SamplesPerSec);
            Assert.Equal((ushort)2, format.Channels);
            return FailOpen ? 2u : 0u;
        }
        public uint Prepare(IntPtr handle, IntPtr header)
        {
            if (prepared.Count + 1 == FailPreparation) { Calls.Add("prepare-failed"); return 7; }
            prepared.Add(header); Calls.Add("prepare"); return 0;
        }
        public uint Write(IntPtr handle, IntPtr header)
        {
            var value = Marshal.PtrToStructure<WaveOutFeedbackOutput.WaveHeader>(header);
            value.Flags = 0x10; Marshal.StructureToPtr(value, header, false);
            Calls.Add("write"); return 0;
        }
        public uint Reset(IntPtr handle)
        {
            foreach (var header in prepared)
            {
                var value = Marshal.PtrToStructure<WaveOutFeedbackOutput.WaveHeader>(header);
                value.Flags = 1; Marshal.StructureToPtr(value, header, false);
            }
            Calls.Add("reset"); return 0;
        }
        public uint Unprepare(IntPtr handle, IntPtr header)
        {
            Assert.DoesNotContain(prepared, item => item == header && (Marshal.PtrToStructure<WaveOutFeedbackOutput.WaveHeader>(item).Flags & 0x10) != 0);
            prepared.Remove(header); Calls.Add("unprepare"); return 0;
        }
        public uint Close(IntPtr handle) { Assert.Empty(prepared); Calls.Add("close"); return 0; }
    }
}
