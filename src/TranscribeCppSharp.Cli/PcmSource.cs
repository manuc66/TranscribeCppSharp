#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp.Cli;

/// <summary>
/// Random access to 16 kHz mono f32 PCM, without holding the whole input in
/// memory.
/// </summary>
/// <remarks>
/// The command used to decode the entire file into one <c>float[]</c> and then
/// slice windows out of it. That array grows with the audio: 219 MiB for one
/// hour at 16 kHz mono, and it is live for the whole run next to a model that is
/// already 617 MB. Since the command consumes the audio strictly window by
/// window, in order, it never needs more than one window at a time.
///
/// Two implementations, because the input reaches PCM two different ways:
/// <see cref="WavPcmSource"/> reads a WAV in place, and
/// <see cref="FfmpegPcmSource"/> reads the file ffmpeg decoded once up front.
/// Both keep the same peak: one window.
/// </remarks>
internal abstract class PcmSource : IDisposable
{
    /// <summary>Total number of samples available, at 16 kHz mono.</summary>
    internal abstract long LengthSamples { get; }

    /// <summary>
    /// Reads <paramref name="count"/> samples starting at
    /// <paramref name="offset"/>, as a fresh array. Reads past the end are
    /// clamped, so the result can be shorter than <paramref name="count"/>.
    /// </summary>
    /// <param name="offset">First sample to read, in samples from the start.</param>
    /// <param name="count">How many samples to ask for.</param>
    /// <returns>The samples read; never null, possibly empty.</returns>
    internal abstract float[] ReadWindow(long offset, int count);

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reads exactly <paramref name="destination"/>.Length bytes at
    /// <paramref name="fileOffset"/>, throwing if the file is shorter.
    /// </summary>
    /// <remarks>
    /// The loop is here rather than in the callers because <see cref="Stream"/>
    /// .Read is allowed to return fewer bytes than asked for, and a single short
    /// read in the middle of a window would silently truncate the audio handed to
    /// the model — a quiet, wrong transcription rather than a visible failure.
    /// </remarks>
    protected static void ReadExactlyAt(FileStream stream, long fileOffset, Span<byte> destination)
    {
        stream.Position = fileOffset;
        while (!destination.IsEmpty)
        {
            int read = stream.Read(destination);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    $"expected {destination.Length} more bytes at offset {fileOffset} but the file ended");
            }

            destination = destination[read..];
        }
    }
}

/// <summary>
/// Reads 16 kHz mono f32 PCM out of a 16-bit PCM WAV, straight from the file,
/// converting each window as it is asked for.
/// </summary>
/// <remarks>
/// Only the RIFF header is parsed, to find where the sample data starts and how
/// long it is. That walk is similar to the one in
/// <c>PcmExtensions.ReadWavToPcm</c>, which lives in the wrapper and is public
/// API for library callers; it is not exposed there, so this parses the handful
/// of chunks it needs rather than widening the library's surface for one caller.
/// The accepted formats are the same set, and the same errors, so a file this
/// rejects is a file the previous code path rejected too.
/// </remarks>
internal sealed class WavPcmSource : PcmSource
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

    /// <inheritdoc/>
    internal override long LengthSamples => DataSize / (BytesPerSample * channels);

    /// <summary>
    /// Opens <paramref name="path"/>, or returns null when it is not a WAV this
    /// can read directly, so the caller can fall back to ffmpeg.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file is a WAV, but not one this can read (not PCM, not 16-bit, or not
    /// 16 kHz). That is a different outcome from "not a WAV": the ffmpeg path
    /// can resample and downmix those, so they are handed over rather than
    /// rejected.
    /// </exception>
    internal static WavPcmSource? TryOpen(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (stream.Length < 12)
            {
                return null;
            }

            // RIFF is "RIFF", then a 4-byte file size, then "WAVE". Reading the
            // two codes back to back puts the file size where "WAVE" should be.
            string riff = ReadFourCC(reader);
            _ = reader.ReadInt32(); // file size, ignored
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
            // Not readable as a WAV for any reason: let ffmpeg have it, or fail
            // there with one line. This mirrors Load(), which also fell through to
            // ffmpeg on these.
            stream.Dispose();
            return null;
        }
        catch (InvalidDataException)
        {
            // A readable WAV this cannot use directly: resampling and downmixing
            // are ffmpeg's job, so fall through rather than reject the file.
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
                // The full 16-byte PCM fmt chunk, field by field. Reading only
                // the first three and skipping the rest by arithmetic is how this
                // got the sample width wrong: bitsPerSample was never read, so a
                // 24-bit file was accepted and then read as 16-bit, which is
                // quiet nonsense rather than an error.
                audioFormat = ReadInt16(reader);
                channels = ReadInt16(reader);
                sampleRate = ReadInt32(reader);
                _ = ReadInt32(reader);                          // byte rate
                _ = ReadInt16(reader);                          // block align
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
                stream.Position++; // WAV padding byte
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

    /// <inheritdoc/>
    internal override float[] ReadWindow(long offset, int count)
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

    /// <inheritdoc/>
    public override void Dispose()
    {
        stream.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Reads 16 kHz mono f32 PCM out of the raw f32 file ffmpeg produced, seeking to
/// each window instead of holding the decoded audio.
/// </summary>
internal sealed class FfmpegPcmSource : PcmSource
{
    private const int BytesPerSample = sizeof(float);

    private readonly string tempFile;
    private readonly FileStream stream;
    private readonly long lengthSamples;

    internal FfmpegPcmSource(string tempFile, FileStream stream, long lengthSamples)
    {
        this.tempFile = tempFile;
        this.stream = stream;
        this.lengthSamples = lengthSamples;
    }

    /// <inheritdoc/>
    internal override long LengthSamples => lengthSamples;

    /// <inheritdoc/>
    internal override float[] ReadWindow(long offset, int count)
    {
        long available = lengthSamples - offset;
        if (available <= 0 || count <= 0)
        {
            return [];
        }

        int toRead = (int)Math.Min(count, available);
        var pcm = new float[toRead];

        // Straight into the float[] through its byte view: the file is the same
        // endianness as the process, because ffmpeg was told to write f32le and
        // the platform here is little-endian. Guarded anyway, so a big-endian
        // host does not silently read swapped samples.
        Span<float> samples = pcm;
        ReadExactlyAt(stream, offset * BytesPerSample, MemoryMarshal.AsBytes(samples));
        return pcm;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        stream.Dispose();
        try
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file must not fail the run.
        }

        base.Dispose();
    }
}
