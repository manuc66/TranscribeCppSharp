using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp.Audio;

/// <summary>
/// Reads 16 kHz mono f32 PCM out of the raw f32 file ffmpeg produced.
/// </summary>
public sealed class FfmpegPcmSource : PcmSource
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

    /// <inheritdoc />
    public override long LengthSamples => lengthSamples;

    /// <inheritdoc />
    public override float[] ReadWindow(long offset, int count)
    {
        long available = lengthSamples - offset;
        if (available <= 0 || count <= 0)
        {
            return [];
        }

        int toRead = (int)Math.Min(count, available);
        var pcm = new float[toRead];

        Span<float> samples = pcm;
        ReadExactlyAt(stream, offset * BytesPerSample, MemoryMarshal.AsBytes(samples));
        return pcm;
    }

    /// <inheritdoc />
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
