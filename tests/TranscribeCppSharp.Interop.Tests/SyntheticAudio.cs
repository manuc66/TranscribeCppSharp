#nullable enable

using System.Text;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Writes a 16 kHz mono 16-bit PCM WAV. Used to build audio that is longer than a
/// VAD window, so the windowed path can be exercised with a real file rather than
/// the 11 s sample.
/// </summary>
internal static class SyntheticAudio
{
    public static string WriteWav(string path, float[] pcm, int sampleRate = 16000)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

        int dataBytes = pcm.Length * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                 // fmt chunk size
        w.Write((short)1);           // PCM
        w.Write((short)1);           // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);     // byte rate
        w.Write((short)2);           // block align
        w.Write((short)16);          // bits per sample
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);

        foreach (float sample in pcm)
        {
            w.Write((short)Math.Clamp((int)Math.Round(sample * 32767), short.MinValue, short.MaxValue));
        }

        return path;
    }

    /// <summary>Repeats <paramref name="pcm"/> <paramref name="times"/>, optionally with a gap of silence between copies.</summary>
    public static float[] Repeat(float[] pcm, int times, int gapSamples = 0, int sampleRate = 16000)
    {
        var result = new float[(pcm.Length + gapSamples) * times];
        for (int i = 0; i < times; i++)
        {
            pcm.CopyTo(result, i * (pcm.Length + gapSamples));
        }

        return result;
    }
}
