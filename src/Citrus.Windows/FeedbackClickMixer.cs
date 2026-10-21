namespace Citrus.Windows;

/// <summary>Fixed voice pool: new clicks overlap existing PCM tails; overload never queues stale input.</summary>
internal sealed class FeedbackClickMixer
{
    private readonly short[] pcm;
    private readonly int[] positions = new int[64];

    public FeedbackClickMixer(short[] pcm)
    {
        if (pcm.Length == 0 || pcm.Length % 2 != 0) throw new ArgumentException("Stereo PCM frames are required.", nameof(pcm));
        this.pcm = pcm;
        Clear();
    }

    public bool HasVoices
    {
        get { foreach (int position in positions) if (position >= 0) return true; return false; }
    }

    public bool TryStart()
    {
        for (int i = 0; i < positions.Length; i++)
        {
            if (positions[i] >= 0) continue;
            positions[i] = 0;
            return true;
        }
        return false;
    }

    public void Clear() => Array.Fill(positions, -1);

    public void Render(Span<short> output)
    {
        for (int i = 0; i < output.Length; i++)
        {
            int sum = 0;
            for (int voice = 0; voice < positions.Length; voice++)
            {
                int position = positions[voice];
                if (position < 0) continue;
                sum += pcm[position++];
                positions[voice] = position == pcm.Length ? -1 : position;
            }
            // Normal clicks pass through exactly. Only extreme coincident bursts enter this
            // smooth knee; avoid both PCM overflow and the flat top of hard clipping.
            int magnitude = Math.Abs(sum);
            output[i] = magnitude <= 24576 ? (short)sum : (short)(Math.Sign(sum) *
                Math.Min(32766, 24576 + 8191 * (1 - Math.Exp(-(magnitude - 24576) / 8191.0))));
        }
    }
}
