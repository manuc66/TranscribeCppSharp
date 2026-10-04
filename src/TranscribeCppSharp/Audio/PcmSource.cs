using System;
using System.IO;

namespace TranscribeCppSharp.Audio;

/// <summary>
/// Random access to 16 kHz mono f32 PCM, without holding the whole input in memory.
/// </summary>
public abstract class PcmSource : IDisposable
{
    /// <summary>Total number of samples available, at 16 kHz mono.</summary>
    public abstract long LengthSamples { get; }

    /// <summary>
    /// Reads <paramref name="count"/> samples starting at <paramref name="offset"/>, as a fresh array.
    /// </summary>
    public abstract float[] ReadWindow(long offset, int count);

    /// <inheritdoc />
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reads exactly <paramref name="destination"/>.Length bytes at
    /// <paramref name="fileOffset"/>, throwing if the file is shorter.
    /// </summary>
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
