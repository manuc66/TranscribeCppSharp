#nullable enable

using System;
using System.IO;
using System.Text;
using TranscribeCppSharp;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the WAV reader's chunked conversion.
///
/// <see cref="PcmExtensions.ReadWavToPcm"/> used to buffer the whole data chunk
/// in a byte[] and convert it in one pass. It now reads and converts 64 KiB at a
/// time, so what these cover is what the one-pass version could never reach: a
/// data chunk spanning several conversion chunks, with a partial final chunk
/// and a per-frame stride in the stereo downmix that has to survive the
/// boundary.
/// </summary>
/// <remarks>
/// Non-parallel: one test here measures
/// <see cref="GC.GetTotalAllocatedBytes(bool)"/>, which is process-wide, so
/// allocations from a test running concurrently would land in its number.
/// </remarks>
[Collection(nameof(PcmExtensionsChunkedTests))]
public class PcmExtensionsChunkedTests
{
    /// <summary>Frames per conversion chunk for mono: 64 KiB / 2 bytes.</summary>
    private const int ChunkFrames = 32 * 1024;

    [Fact]
    public void ReadWavToPcm_Mono_SpanningSeveralChunks_MatchesAReferenceDecoder()
    {
        // Three full chunks plus a partial tail, so the last chunk is the only
        // one that ends early.
        int frames = (ChunkFrames * 3) + 1234;
        byte[] data = BuildInterleavedData(frames, numChannels: 1);
        using var temp = new TempWorkspace();
        string path = WriteWav(temp.Combine("big-mono.wav"), data, numChannels: 1);

        float[] pcm = PcmExtensions.ReadWavToPcm(path);

        Assert.Equal(frames, pcm.Length);
        for (int i = 0; i < frames; i++)
        {
            Assert.Equal(ExpectedMono(data, i), pcm[i], 6);
        }
    }

    [Fact]
    public void ReadWavToPcm_Stereo_SpanningSeveralChunks_DownmixesEveryFrame()
    {
        // The downmix indexes with a per-frame stride, so a frame that
        // straddles a chunk boundary is the case that can go wrong.
        int frames = (ChunkFrames * 2) + 777;
        byte[] data = BuildInterleavedData(frames, numChannels: 2);
        using var temp = new TempWorkspace();
        string path = WriteWav(temp.Combine("big-stereo.wav"), data, numChannels: 2);

        float[] pcm = PcmExtensions.ReadWavToPcm(path);

        Assert.Equal(frames, pcm.Length);
        for (int i = 0; i < frames; i++)
        {
            Assert.Equal(ExpectedStereo(data, i, channels: 2), pcm[i], 6);
        }
    }

    [Fact]
    public void ReadWavToPcm_ExactlyOneChunkPlusOne_MatchesAReferenceDecoder()
    {
        // The off-by-one case for the "is the chunk full" test: one sample past
        // a chunk boundary.
        int frames = ChunkFrames + 1;
        byte[] data = BuildInterleavedData(frames, numChannels: 1);
        using var temp = new TempWorkspace();
        string path = WriteWav(temp.Combine("one-plus-one.wav"), data, numChannels: 1);

        float[] pcm = PcmExtensions.ReadWavToPcm(path);

        Assert.Equal(frames, pcm.Length);
        for (int i = 0; i < frames; i++)
        {
            Assert.Equal(ExpectedMono(data, i), pcm[i], 6);
        }
    }

    [Fact]
    public void ReadWavToPcm_ExactlyOneChunk_MatchesAReferenceDecoder()
    {
        // The other side of the boundary: a payload that fills the chunk and
        // ends there, which must not be read as a short chunk.
        int frames = ChunkFrames;
        byte[] data = BuildInterleavedData(frames, numChannels: 1);
        using var temp = new TempWorkspace();
        string path = WriteWav(temp.Combine("exactly-one.wav"), data, numChannels: 1);

        float[] pcm = PcmExtensions.ReadWavToPcm(path);

        Assert.Equal(frames, pcm.Length);
        for (int i = 0; i < frames; i++)
        {
            Assert.Equal(ExpectedMono(data, i), pcm[i], 6);
        }
    }

    [Fact]
    public void ReadWavToPcm_TruncatedDataChunk_IsClampedToWhatTheFileHolds()
    {
        // The header announces more samples than the file contains. The reader
        // clamps to the bytes actually present (as it did before this change)
        // and returns exactly those samples.
        int frames = 64;
        byte[] data = BuildInterleavedData(frames, numChannels: 1);
        using var temp = new TempWorkspace();
        string path = WriteWav(temp.Combine("truncated.wav"), data, numChannels: 1);

        // Keep the 44-byte header and only half the payload.
        byte[] full = File.ReadAllBytes(path);
        const int HeaderBytes = 44;
        int keep = HeaderBytes + (frames / 2 * 2);
        byte[] cut = new byte[keep];
        Array.Copy(full, cut, keep);
        File.WriteAllBytes(path, cut);

        float[] pcm = PcmExtensions.ReadWavToPcm(path);

        Assert.Equal(frames / 2, pcm.Length);
        for (int i = 0; i < pcm.Length; i++)
        {
            Assert.Equal(ExpectedMono(data, i), pcm[i], 6);
        }
    }

    [Fact]
    public void ReadWavToPcm_AllocatesLittleBeyondTheResult()
    {
        // Guards the allocation claim behind this change: the result is sized
        // from the data chunk, and nothing of that size is allocated alongside
        // it. The old one-pass reader added a byte[] of the same length, so it
        // came to 1.5x the result.
        int frames = ChunkFrames + 999;
        byte[] data = BuildInterleavedData(frames, numChannels: 1);
        using var temp = new TempWorkspace();
        string path = WriteWav(temp.Combine("length.wav"), data, numChannels: 1);

        // Warm up first: ArrayPool allocates its bucket on first use, which is a
        // one-time cost for the process, not a per-read one. Measured on a
        // 33,767-sample file, the first call allocates 1.54x the result and every
        // call after it 1.03x, the difference being the 64 KiB pool bucket.
        PcmExtensions.ReadWavToPcm(path);

        long before = GC.GetTotalAllocatedBytes(precise: true);
        float[] pcm = PcmExtensions.ReadWavToPcm(path);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.Equal(frames, pcm.Length);

        // One float per sample, plus slack for the FileStream buffer and the
        // header reads. The old reader's byte[] alone was frames * 2 bytes, so
        // any bound under that catches the regression.
        long resultBytes = (long)pcm.Length * sizeof(float);
        Assert.True(
            allocated < resultBytes + 8192,
            $"allocated {allocated} bytes for a {resultBytes} byte result");
    }

    // --- helpers ------------------------------------------------------------

    /// <summary>
    /// This class's tests share a collection so they never run alongside each
    /// other; see the type remarks for why.
    /// </summary>
    [CollectionDefinition(nameof(PcmExtensionsChunkedTests), DisableParallelization = true)]
    public class PcmExtensionsChunkedTestGroup
    {
    }

    /// <summary>
    /// Interleaved 16-bit LE sample data whose value depends on both the frame
    /// index and the channel, so a decoder that loses, repeats or shifts a frame
    /// is caught rather than passed.
    /// </summary>
    private static byte[] BuildInterleavedData(int frames, int numChannels)
    {
        var data = new byte[frames * 2 * numChannels];
        for (int f = 0; f < frames; f++)
        {
            for (int ch = 0; ch < numChannels; ch++)
            {
                int value = (f * 37) + (ch * 7919);
                short s = (short)(value % 30000 - 15000);
                int offset = ((f * numChannels) + ch) * 2;
                data[offset] = (byte)(s & 0xFF);
                data[offset + 1] = (byte)((s >> 8) & 0xFF);
            }
        }

        return data;
    }

    private static float ExpectedMono(byte[] data, int frame)
    {
        short s = BitConverter.ToInt16(data, frame * 2);
        return s / 32768f;
    }

    private static float ExpectedStereo(byte[] data, int frame, int channels)
    {
        int sum = 0;
        for (int ch = 0; ch < channels; ch++)
        {
            sum += BitConverter.ToInt16(data, ((frame * channels) + ch) * 2);
        }

        return sum / (float)channels / 32768f;
    }

    private static string WriteWav(string path, byte[] data, int numChannels)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + data.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)numChannels);
        w.Write(16000);
        w.Write(16000 * numChannels * 2);
        w.Write((short)(numChannels * 2));
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(data.Length);
        w.Write(data);
        return path;
    }
}
