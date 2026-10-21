using System.Reflection;

namespace Citrus.Windows.Tests;

public sealed class FeedbackClickMixerTests
{
    [Fact]
    public void Starting_another_click_preserves_the_first_clicks_remaining_samples()
    {
        var mixer = new FeedbackClickMixer([100, 100, 200, 200, 300, 300, 400, 400]);
        Assert.True(mixer.TryStart());
        short[] first = new short[4];
        mixer.Render(first);
        Assert.Equal<short>([100, 100, 200, 200], first);

        Assert.True(mixer.TryStart());
        short[] overlap = new short[8];
        mixer.Render(overlap);
        Assert.Equal<short>([400, 400, 600, 600, 300, 300, 400, 400], overlap);
        Assert.False(mixer.HasVoices);
    }

    [Fact]
    public void Forty_eight_one_millisecond_transitions_mix_every_original_sample_tail()
    {
        byte[] wave = (byte[])typeof(FeedbackAudio).GetMethod("CreateWave", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [FeedbackCue.Selection])!;
        Assert.Equal(11564, wave.Length); // 44-byte header + 60 ms, 48 kHz, stereo PCM16.
        short[] original = new short[5760];
        Buffer.BlockCopy(wave, 44, original, 0, 11520);
        var mixer = new FeedbackClickMixer(original);
        int[] expected = new int[original.Length + 47 * 96];
        short[] actual = new short[expected.Length];
        for (int click = 0; click < 48; click++)
        {
            Assert.True(mixer.TryStart());
            for (int i = 0; i < original.Length; i++) expected[click * 96 + i] += original[i];
            mixer.Render(actual.AsSpan(click * 96, 96));
        }
        mixer.Render(actual.AsSpan(48 * 96));

        Assert.All(expected, value => Assert.InRange(value, -24576, 24576));
        Assert.Equal(expected, actual.Select(value => (int)value));
        Assert.False(mixer.HasVoices);
    }

    [Fact]
    public void A_full_voice_pool_drops_excess_input_without_restarting_or_queueing_old_clicks()
    {
        var mixer = new FeedbackClickMixer([100, 100, 200, 200]);
        for (int i = 0; i < 64; i++) Assert.True(mixer.TryStart());
        for (int i = 0; i < 1000; i++) Assert.False(mixer.TryStart());
        short[] output = new short[6];
        mixer.Render(output);
        Assert.Equal<short>([6400, 6400, 12800, 12800, 0, 0], output);
        Assert.False(mixer.HasVoices);

        Assert.True(mixer.TryStart());
        mixer.Render(output);
        Assert.Equal<short>([100, 100, 200, 200, 0, 0], output);
    }

    [Fact]
    public void Extreme_overlap_is_soft_limited_without_integer_wrap_or_flat_clipping()
    {
        var mixer = new FeedbackClickMixer([20000, -20000, 30000, -30000]);
        for (int i = 0; i < 64; i++) Assert.True(mixer.TryStart());
        short[] output = new short[4];
        mixer.Render(output);
        Assert.InRange(output[0], (short)24577, (short)32766);
        Assert.Equal(-output[0], output[1]);
        Assert.InRange(output[2], output[0], (short)32766);
        Assert.Equal(-output[2], output[3]);
    }

    [Fact]
    public void Clear_discards_every_active_tail_and_the_next_render_is_silent()
    {
        var mixer = new FeedbackClickMixer([100, 100, 200, 200]);
        mixer.TryStart(); mixer.TryStart();
        mixer.Clear();
        short[] output = [999, 999, 999, 999];
        mixer.Render(output);
        Assert.Equal<short>([0, 0, 0, 0], output);
        Assert.False(mixer.HasVoices);
    }
}
