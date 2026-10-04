using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace TranscribeCppSharp.Audio;

/// <summary>
/// Reads 16 kHz mono f32 PCM out of a 16-bit PCM WAV, straight from the file.
/// </summary>
public sealed class WavPcmSource : PcmSource
{
    private const int BytesPerSample = 2;

    private readonly FileStream stream;
    private readonly int channels;

    private WavPcmSource(FileStream stream, long dataStart, int dataSize, int channels)
    {
        this.stream = stream;
        this.channels = channels;
        DataStart = dataStart;
        DataSize = dataSize;
    }

    private long DataStart { get; }

    private int DataSize { get; }

    /// <inheritdoc />
    public override long LengthSamples => DataSize / (BytesPerSample * channels);

    /// <summary>
    /// Opens <paramref name="path"/>, or returns null when it is not a WAV this
    /// can read directly, so the caller can fall back to ffmpeg.
    /// </summary>
    public static WavPcmSource? TryOpen(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (stream.Length < 12)
            {
                return null;
            }

            string riff = ReadFourCC(reader);
            _ = reader.ReadInt32();
            string wave = ReadFourCC(reader);
            if (riff != "RIFF" || wave != "WAVE")
            {
                return null;
            }

            (long dataStart, int dataSize, int channels) = WalkChunks(stream, reader);

            if (channels <= 0)
            {
                return null;
            }

            return new WavPcmSource(stream, dataStart, dataSize, channels);
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentException or IOException
                                      or UnauthorizedAccessException or NotSupportedException)
        {
            stream.Dispose();
            return null;
        }
        catch (InvalidDataException)
        {
            stream.Dispose();
            throw;
        }
    }

    private static string ReadFourCC(BinaryReader reader)
    {
        Span<byte> four = stackalloc byte[4];
        int read = 0;
        while (read < 4)
        {
            int n = reader.BaseStream.Read(four[read..]);
            if (n == 0)
            {
                throw new EndOfStreamException("truncated RIFF header");
            }

            read += n;
        }

        return Encoding.ASCII.GetString(four);
    }

    private static (long DataStart, int DataSize, int Channels) WalkChunks(Stream stream, BinaryReader reader)
    {
        int channels = 0;
        int bitsPerSample = 0;
        int sampleRate = 0;
        int audioFormat = 0;

        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 8)
            {
                break;
            }

            string id = ReadFourCC(reader);
            int size = ReadInt32(reader);
            if (size < 0)
            {
                throw new InvalidDataException($"Negative chunk size for '{id}'");
            }

            if (id == "fmt " && size >= 16)
            {
                audioFormat = ReadInt16(reader);
                channels = ReadInt16(reader);
                sampleRate = ReadInt32(reader);
                _ = ReadInt32(reader);
                _ = ReadInt16(reader);
                bitsPerSample = ReadInt16(reader);
                Skip(stream, size - 16);
            }
            else if (id == "data")
            {
                long start = stream.Position;
                if (audioFormat != 1)
                {
                    throw new InvalidDataException(
                        $"Unsupported audio format {audioFormat} (expected PCM = 1)");
                }

                if (bitsPerSample != 16)
                {
                    throw new InvalidDataException($"Expected 16-bit, got {bitsPerSample}-bit");
                }

                if (sampleRate != 16_000)
                {
                    throw new InvalidDataException($"Expected 16kHz, got {sampleRate}Hz");
                }

                return (start, size, channels);
            }
            else
            {
                Skip(stream, size);
            }

            if (size % 2 != 0)
            {
                stream.Position++;
            }
        }

        throw new InvalidDataException("WAV file has no data chunk");
    }

    private static int ReadInt32(BinaryReader reader)
    {
        Span<byte> b = stackalloc byte[4];
        reader.BaseStream.ReadExactly(b);
        return BinaryPrimitives.ReadInt32LittleEndian(b);
    }

    private static short ReadInt16(BinaryReader reader)
    {
        Span<byte> b = stackalloc byte[2];
        reader.BaseStream.ReadExactly(b);
        return BinaryPrimitives.ReadInt16LittleEndian(b);
    }

    private static void Skip(Stream stream, long count)
    {
        if (count > 0)
        {
            stream.Position += count;
        }
    }

    /// <inheritdoc />
    public override float[] ReadWindow(long offset, int count)
    {
        long available = LengthSamples - offset;
        if (available <= 0 || count <= 0)
        {
            return [];
        }

        int toRead = (int)Math.Min(count, available);
        int frameBytes = BytesPerSample * channels;
        var raw = ArrayPool<byte>.Shared.Rent(toRead * frameBytes);
        try
        {
            ReadExactlyAt(stream, DataStart + (offset * frameBytes), raw.AsSpan(0, toRead * frameBytes));

            var pcm = new float[toRead];
            if (channels == 1)
            {
                for (int i = 0; i < toRead; i++)
                {
                    pcm[i] = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(i * 2, 2)) / 32768f;
                }
            }
            else
            {
                for (int i = 0; i < toRead; i++)
                {
                    int sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        sum += BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(((i * channels) + ch) * 2, 2));
                    }

                    pcm[i] = sum / (float)channels / 32768f;
                }
            }

            return pcm;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        stream.Dispose();
        base.Dispose();
    }
}
