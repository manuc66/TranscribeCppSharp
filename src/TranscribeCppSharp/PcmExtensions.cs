#nullable enable

using System;
using System.IO;

namespace TranscribeCppSharp;

/// <summary>
/// Extension methods for Span-based PCM loading from WAV files.
/// </summary>
public static class PcmExtensions
{
    /// <summary>
    /// Read a 16-bit WAV file and convert to 16 kHz mono float PCM.
    /// </summary>
    public static float[] ReadWavToPcm(string wavPath)
    {
        using var fs = File.OpenRead(wavPath);
        using var br = new BinaryReader(fs);

        var riff = new string(br.ReadChars(4));
        var fileSize = br.ReadInt32();
        var wave = new string(br.ReadChars(4));
        if (riff != "RIFF" || wave != "WAVE")
            throw new InvalidDataException("Not a WAV file");

        int sampleRate = 0, bitsPerSample = 0, numChannels = 0;
        int dataSize = 0;
        long dataStart = 0;

        while (fs.Position < fs.Length)
        {
            var chunkId = new string(br.ReadChars(4));
            var chunkSize = br.ReadInt32();

            if (chunkId == "fmt ")
            {
                br.ReadInt16(); // audio format
                numChannels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32(); // byte rate
                br.ReadInt16(); // block align
                bitsPerSample = br.ReadInt16();
                fs.Position += chunkSize - 16;
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
                dataStart = fs.Position;
                break;
            }
            else
            {
                fs.Position += chunkSize;
            }
        }

        if (sampleRate != 16000)
            throw new InvalidDataException($"Expected 16kHz, got {sampleRate}Hz");
        if (bitsPerSample != 16)
            throw new InvalidDataException($"Expected 16-bit, got {bitsPerSample}-bit");

        fs.Position = dataStart;
        var samples16 = new short[dataSize / 2];
        for (int i = 0; i < samples16.Length; i++)
            samples16[i] = br.ReadInt16();

        var nSamples = samples16.Length / numChannels;
        var pcm = new float[nSamples];
        for (int i = 0; i < nSamples; i++)
        {
            float sum = 0;
            for (int ch = 0; ch < numChannels; ch++)
                sum += samples16[i * numChannels + ch];
            pcm[i] = sum / numChannels / 32768f;
        }

        return pcm;
    }
}
