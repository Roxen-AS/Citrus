using System.Reflection;

namespace Citrus.Windows.Tests;

public sealed class FeedbackAudioTests
{
    [Theory]
    [InlineData(1000)]
    [InlineData(10000000)]
    public void Every_rapid_selection_reaches_the_overlap_player_without_a_duration_throttle(long frequency)
    {
        var player = new SilentPlayer();
        long now = 100;
        using var audio = new FeedbackAudio(Task.FromResult<FeedbackAudio.IPlayer>(player), new ManualClock(() => now, frequency));
        for (int i = 0; i < 48; i++)
        {
            audio.Play(FeedbackCue.Selection);
            now += frequency / 1000;
        }
        Assert.Equal(48, player.Starts);
        Assert.Equal(now - frequency / 1000, LastSelection(audio));
    }

    [Fact]
    public void Selections_during_loading_are_dropped_instead_of_replayed_later()
    {
        var loading = new TaskCompletionSource<FeedbackAudio.IPlayer>();
        var player = new SilentPlayer();
        long now = 100;
        using var audio = new FeedbackAudio(loading.Task, new ManualClock(() => now));
        for (int i = 0; i < 100; i++)
        {
            audio.Play(FeedbackCue.Selection);
            now += 50;
        }
        Assert.Equal(long.MinValue, LastSelection(audio));

        loading.SetResult(player);
        Assert.Equal(0, player.Starts);
        audio.Play(FeedbackCue.Selection);
        Assert.Equal(1, player.Starts);
    }

    [Fact]
    public void Consecutive_selections_in_the_same_clock_tick_are_not_lost()
    {
        long now = 100;
        var player = new SilentPlayer { Starting = () => now += 10 };
        using var audio = new FeedbackAudio(Task.FromResult<FeedbackAudio.IPlayer>(player), new ManualClock(() => now));
        audio.Play(FeedbackCue.Selection);
        audio.Play(FeedbackCue.Selection);
        audio.Play(FeedbackCue.Selection);
        Assert.Equal(3, player.Starts);
    }

    [Fact]
    public void Stop_interrupts_the_current_tick_and_allows_a_fresh_session_tick()
    {
        var player = new SilentPlayer();
        using var audio = new FeedbackAudio(Task.FromResult<FeedbackAudio.IPlayer>(player), new ManualClock(() => 100));
        audio.Play(FeedbackCue.Selection);
        audio.Stop();
        Assert.Equal(1, player.Stops);
        audio.Play(FeedbackCue.Selection);
        Assert.Equal(2, player.Starts);
    }

    [Fact]
    public void Disabled_audio_stops_playback_and_cannot_start_a_new_tick_until_enabled()
    {
        var player = new SilentPlayer();
        using var audio = new FeedbackAudio(Task.FromResult<FeedbackAudio.IPlayer>(player), new ManualClock(() => 100));
        audio.Play(FeedbackCue.Selection);
        audio.Enabled = false;
        audio.Play(FeedbackCue.Selection);
        Assert.Equal(1, player.Stops);
        Assert.Equal(1, player.Starts);
        audio.Enabled = true;
        audio.Play(FeedbackCue.Selection);
        Assert.Equal(2, player.Starts);
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("Disable")]
    [InlineData("Dispose")]
    public async Task Ending_a_generation_during_loading_cannot_replay_an_old_tick(string ending)
    {
        var loading = new TaskCompletionSource<FeedbackAudio.IPlayer>();
        var player = new SilentPlayer();
        using var audio = new FeedbackAudio(loading.Task, new ManualClock(() => 100));
        audio.Play(FeedbackCue.Selection);
        switch (ending)
        {
            case "Stop": audio.Stop(); break;
            case "Disable": audio.Enabled = false; break;
            case "Dispose": audio.Dispose(); break;
        }
        loading.SetResult(player);
        if (ending == "Dispose") await player.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, player.Starts);
    }

    [Fact]
    public async Task Dispose_stops_and_releases_the_player_once_and_rejects_later_ticks()
    {
        var player = new SilentPlayer();
        var audio = new FeedbackAudio(Task.FromResult<FeedbackAudio.IPlayer>(player), new ManualClock(() => 100));
        audio.Play(FeedbackCue.Selection);
        audio.Dispose();
        audio.Dispose();
        await player.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        audio.Play(FeedbackCue.Selection);
        Assert.Equal(1, player.Starts);
        Assert.Equal(1, player.Stops);
        Assert.Equal(1, player.Disposals);
    }

    [Fact]
    public void Feedback_remains_selection_only()
    {
        var player = new SilentPlayer();
        using var audio = new FeedbackAudio(Task.FromResult<FeedbackAudio.IPlayer>(player), new ManualClock(() => 100));
        audio.Play(FeedbackCue.Open);
        audio.Play(FeedbackCue.Success);
        audio.Play(FeedbackCue.Error);
        Assert.Equal(0, player.Starts);
        audio.Play(FeedbackCue.Selection);
        Assert.Equal(1, player.Starts);
    }

    [Fact]
    public void Optional_player_failure_does_not_block_a_later_selection()
    {
        var player = new SilentPlayer { Starting = () => throw new InvalidOperationException("Device unavailable") };
        using var audio = new FeedbackAudio(Task.FromResult<FeedbackAudio.IPlayer>(player), new ManualClock(() => 100));
        audio.Play(FeedbackCue.Selection);
        player.Starting = null;
        audio.Play(FeedbackCue.Selection);
        Assert.Equal(1, player.Starts);
    }

    [Fact]
    public void Missing_endpoint_keeps_feedback_optional_and_does_not_queue_selections()
    {
        using var audio = new FeedbackAudio(Task.FromException<FeedbackAudio.IPlayer>(new InvalidOperationException("No render endpoint")), new ManualClock(() => 100));
        for (int i = 0; i < 100; i++) audio.Play(FeedbackCue.Selection);
        audio.Stop();
        audio.Enabled = false;
        Assert.Equal(long.MinValue, LastSelection(audio));
    }

    private static long LastSelection(FeedbackAudio audio) =>
        (long)typeof(FeedbackAudio).GetField("lastSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(audio)!;

    private sealed class ManualClock(Func<long> read, long frequency = 1000) : TimeProvider
    {
        public override long TimestampFrequency => frequency;
        public override long GetTimestamp() => read();
    }

    private sealed class SilentPlayer : FeedbackAudio.IPlayer
    {
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public Action? Starting { get; set; }
        public TaskCompletionSource FirstStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Play() { Starting?.Invoke(); Starts++; FirstStart.TrySetResult(); }
        public void Stop() { Stops++; }
        public void Dispose() { Disposals++; Disposed.TrySetResult(); }
    }
}
