namespace Citrus.Conversion.Tests;

internal static class MediaFixture
{
    // A timestamped input container avoids relying on raw-PCM demuxer timestamp synthesis.
    internal static void WriteWave(string path, IReadOnlyCollection<short> samples)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(checked(36 + samples.Count * 2)); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(checked(samples.Count * 2));
        foreach (short sample in samples) writer.Write(sample);
    }
}
