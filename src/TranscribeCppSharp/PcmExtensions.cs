#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace TranscribeCppSharp;

/// <summary>
/// Extension methods for Span-based PCM loading from WAV files.
/// </summary>
public static class PcmExtensions
{
    /// <summary>
    /// Read a 16-bit PCM WAV file and convert to 16 kHz mono float PCM.
    /// Supports mono and stereo; multi-channel audio is downmixed to mono.
    /// </summary>
    public static float[] ReadWavToPcm(string wavPath)
    {
        using var fs = File.OpenRead(wavPath);
        using var br = new BinaryReader(fs);

        var fmt = ReadFmt(fs, br);
        if (fmt is null)
        {
            throw new InvalidDataException("WAV file has no fmt chunk");
        }

        ValidateFormat(fmt.Value);
        var data = FindDataChunk(fs, br);
        if (data.DataStart == 0 || data.DataSize == 0)
        {
            throw new InvalidDataException("WAV file has no data chunk");
        }

        return ReadAndConvertSamples(fs, data.DataStart, data.DataSize, fmt.Value.NumChannels);
    }

    private static void ReadRiffHeader(BinaryReader br)
    {
        var riff = Encoding.ASCII.GetString(br.ReadBytes(4));
        _ = br.ReadInt32(); // file size (ignored)
        var wave = Encoding.ASCII.GetString(br.ReadBytes(4));
        if (riff != "RIFF" || wave != "WAVE")
        {
            throw new InvalidDataException("Not a WAV file");
        }
    }

    private static WavFormat? ReadFmt(Stream fs, BinaryReader br)
    {
        ReadRiffHeader(br);

        while (fs.Position < fs.Length)
        {
            if (fs.Length - fs.Position < 8)
            {
                break; // need at least id + size
            }

            var chunkId = Encoding.ASCII.GetString(br.ReadBytes(4));
            var chunkSize = br.ReadInt32();
            if (chunkSize < 0)
            {
                throw new InvalidDataException($"Negative chunk size for '{chunkId}'");
            }

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    throw new InvalidDataException("fmt chunk too small");
                }

                var audioFormat = br.ReadInt16();
                var numChannels = br.ReadInt16();
                var sampleRate = br.ReadInt32();
                _ = br.ReadInt32(); // byte rate
                _ = br.ReadInt16(); // block align
                var bitsPerSample = br.ReadInt16();
                fs.Position += chunkSize - 16;
                if (chunkSize % 2 != 0)
                {
                    fs.Position++; // WAV padding byte
                }

                return new WavFormat(audioFormat, numChannels, sampleRate, bitsPerSample);
            }

            SkipChunk(fs, chunkSize);
        }

        return null;
    }

    private static (long DataStart, int DataSize) FindDataChunk(Stream fs, BinaryReader br)
    {
        while (fs.Position < fs.Length)
        {
            if (fs.Length - fs.Position < 8)
            {
                break; // need at least id + size
            }

            var chunkId = Encoding.ASCII.GetString(br.ReadBytes(4));
            var chunkSize = br.ReadInt32();
            if (chunkSize < 0)
            {
                throw new InvalidDataException($"Negative chunk size for '{chunkId}'");
            }

            if (chunkId == "data")
            {
                return (fs.Position, chunkSize);
            }

            SkipChunk(fs, chunkSize);
        }

        return (0, 0);
    }

    private static void SkipChunk(Stream fs, int chunkSize)
    {
        fs.Position += chunkSize;
        if (chunkSize % 2 != 0)
        {
            fs.Position++; // WAV padding byte
        }
    }

    private static void ValidateFormat(WavFormat fmt)
    {
        if (fmt.AudioFormat != 1)
        {
            throw new InvalidDataException(
                $"Unsupported audio format {fmt.AudioFormat} (expected PCM = 1)");
        }

        if (fmt.NumChannels <= 0)
        {
            throw new InvalidDataException(
                $"Invalid channel count: {fmt.NumChannels}");
        }

        if (fmt.SampleRate != 16000)
        {
            throw new InvalidDataException(
                $"Expected 16kHz, got {fmt.SampleRate}Hz");
        }

        if (fmt.BitsPerSample != 16)
        {
            throw new InvalidDataException(
                $"Expected 16-bit, got {fmt.BitsPerSample}-bit");
        }
    }

    private static float[] ReadAndConvertSamples(Stream fs, long dataStart, int dataSize, int numChannels)
    {
        // Clamp dataSize to actual bytes remaining (guard truncated files)
        var remaining = (int)(fs.Length - dataStart);
        if (dataSize > remaining)
        {
            dataSize = remaining;
        }

        fs.Position = dataStart;
        var nSamples = dataSize / (2 * numChannels); // 16-bit = 2 bytes per sample per channel
        var pcm = new float[nSamples];

        // Read raw bytes into a pooled buffer, convert directly
        var byteBuf = new byte[dataSize];
        int bytesRead = 0;
        while (bytesRead < dataSize)
        {
            int read = fs.Read(byteBuf, bytesRead, dataSize - bytesRead);
            if (read == 0)
            {
                break;
            }

            bytesRead += read;
        }

        // Convert 16-bit LE samples to float, downmix if stereo
        if (numChannels == 1)
        {
            for (int i = 0; i < nSamples; i++)
            {
                short s = BitConverter.IsLittleEndian
                    ? (short)(byteBuf[i * 2] | (byteBuf[(i * 2) + 1] << 8))
                    : BinaryPrimitives.ReadInt16BigEndian(byteBuf.AsSpan(i * 2, 2));
                pcm[i] = s / 32768f;
            }
        }
        else
        {
            for (int i = 0; i < nSamples; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < numChannels; ch++)
                {
                    int offset = ((i * numChannels) + ch) * 2;
                    short s = BitConverter.IsLittleEndian
                        ? (short)(byteBuf[offset] | (byteBuf[offset + 1] << 8))
                        : BinaryPrimitives.ReadInt16BigEndian(byteBuf.AsSpan(offset, 2));
                    sum += s;
                }

                pcm[i] = sum / numChannels / 32768f;
            }
        }

        return pcm;
    }

    private readonly record struct WavFormat(short AudioFormat, int NumChannels, int SampleRate, int BitsPerSample);
}
