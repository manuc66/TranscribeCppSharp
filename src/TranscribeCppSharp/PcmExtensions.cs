#nullable enable

using System;
using System.Buffers;
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
    /// How much of the data chunk is read and converted at a time. Large
    /// enough that the read syscalls are not the cost, small enough to stay off
    /// the large object heap and to be reused between calls.
    /// </summary>
    private const int ConvertChunkBytes = 64 * 1024;

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

        // Read and convert chunk by chunk. Buffering the whole data chunk in a
        // byte[] first (as this did) held that array alive next to the float[]
        // for the entire conversion, so a 30-minute file allocated 164 MiB to
        // produce a 109 MiB result — 1.5x, and all of it on the large object
        // heap. Here the result is the only large allocation.
        int frameBytes = 2 * numChannels;
        int chunkBytes = Math.Max(1, ConvertChunkBytes / frameBytes) * frameBytes;
        var buf = ArrayPool<byte>.Shared.Rent(chunkBytes);
        try
        {
            int written = 0;
            while (written < nSamples)
            {
                // A short read is not the end of the chunk, so fill it fully
                // before converting; only a zero read means the file stopped
                // short, in which case the remaining samples stay zero exactly
                // as they did when the reader simply stopped.
                int filled = 0;
                while (filled < chunkBytes)
                {
                    int read = fs.Read(buf, filled, chunkBytes - filled);
                    if (read == 0)
                    {
                        break;
                    }

                    filled += read;
                }

                if (filled == 0)
                {
                    break;
                }

                int frames = filled / frameBytes;
                int max = Math.Min(frames, nSamples - written);
                Convert(buf, max, frameBytes, numChannels, pcm, written);
                written += max;
                if (filled < chunkBytes)
                {
                    break; // end of file reached inside this chunk
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }

        return pcm;
    }

    /// <summary>
    /// Converts <paramref name="frames"/> interleaved 16-bit LE frames from
    /// <paramref name="buf"/> into <paramref name="dest"/>, downmixing
    /// <paramref name="numChannels"/> channels to one average.
    /// </summary>
    private static void Convert(byte[] buf, int frames, int frameBytes, int numChannels, float[] dest, int destOffset)
    {
        if (numChannels == 1)
        {
            for (int i = 0; i < frames; i++)
            {
                dest[destOffset + i] = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(i * 2, 2)) / 32768f;
            }

            return;
        }

        for (int i = 0; i < frames; i++)
        {
            int baseOffset = i * frameBytes;
            int sum = 0;
            for (int ch = 0; ch < numChannels; ch++)
            {
                sum += BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(baseOffset + (ch * 2), 2));
            }

            dest[destOffset + i] = sum / (float)numChannels / 32768f;
        }
    }

    private readonly record struct WavFormat(short AudioFormat, int NumChannels, int SampleRate, int BitsPerSample);
}
