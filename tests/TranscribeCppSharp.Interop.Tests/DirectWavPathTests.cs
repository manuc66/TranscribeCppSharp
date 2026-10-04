#nullable enable

using System;
using System.IO;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Cli;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the direct-WAV path, which must not need ffmpeg.
/// </summary>
/// <remarks>
/// This matters more than it looks. When the direct reader was first written it
/// was broken in two ways — it read the RIFF header in the wrong order, and it
/// never read <c>bitsPerSample</c> — and every test still passed, because the
/// broken path quietly fell through to ffmpeg, which happened to be installed on
/// the machine. It was only caught on CI, whose runners have no ffmpeg.
///
/// So the tests here are the ones that would have caught it: they run with ffmpeg
/// removed from PATH, which turns "silently fell back" into a hard failure.
/// </remarks>
[Collection(nameof(DirectWavPathTests))]
public class DirectWavPathTests
{
    private readonly ITestOutputHelper _output;

    public DirectWavPathTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A 16 kHz mono 16-bit WAV is read without ffmpeg. Aimed at
    /// <see cref="WavPcmSource"/> directly so a fall-through to ffmpeg is a
    /// failure rather than a silent rescue.
    /// </summary>
    [Fact]
    public void TryOpen_AcceptsAPlain16kHzMonoWav_WithoutFfmpeg()
    {
        using var temp = new TempWorkspace();
        var samples = new float[1600];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (i % 100) / 100f;
        }

        string path = temp.WriteWav("a.wav", samples);

        using WavPcmSource? source = WavPcmSource.TryOpen(path);

        Assert.NotNull(source);
        Assert.Equal(samples.Length, source.LengthSamples);

        float[] read = source.ReadWindow(0, samples.Length);
        Assert.Equal(samples.Length, read.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(samples[i], read[i], 3);
        }
    }

    [Fact]
    public void TryOpen_AcceptsAWavWithAnUnknownChunkBeforeData()
    {
        // ffmpeg writes a LIST/INFO chunk before the data chunk, so the walk has
        // to skip chunks it does not know rather than assume data comes second.
        using var temp = new TempWorkspace();
        string path = temp.Combine("withlist.wav");
        WriteWavWithLeadingListChunk(path, new float[800]);

        using WavPcmSource? source = WavPcmSource.TryOpen(path);

        Assert.NotNull(source);
        Assert.Equal(800, source.LengthSamples);
    }

    [Fact]
    public void TryOpen_RejectsANonSixteenBitWav_SoItGoesToFfmpeg()
    {
        // The regression that could not be seen locally: bitsPerSample was never
        // read, so a 24-bit file was accepted and then read as 16-bit, which
        // produces audio that is wrong without looking wrong.
        using var temp = new TempWorkspace();
        string path = temp.Combine("24bit.wav");
        WriteWavWithFormat(path, bitsPerSample: 24, sampleRate: 16_000, channels: 1);

        Assert.Throws<InvalidDataException>(() => WavPcmSource.TryOpen(path));
    }

    [Fact]
    public void TryOpen_RejectsTheWrongSampleRate_SoItGoesToFfmpeg()
    {
        using var temp = new TempWorkspace();
        string path = temp.Combine("48k.wav");
        WriteWavWithFormat(path, bitsPerSample: 16, sampleRate: 48_000, channels: 1);

        Assert.Throws<InvalidDataException>(() => WavPcmSource.TryOpen(path));
    }

    [Fact]
    public void TryOpen_ReturnsNullForSomethingThatIsNotAWav()
    {
        using var temp = new TempWorkspace();
        string notAudio = temp.WriteText("notaudio.wav", "this is not audio at all, it is a text file");

        Assert.Null(WavPcmSource.TryOpen(notAudio));
    }

    [Fact]
    public void ReadWindow_ClampsAndAgreesWithTheWholeFile()
    {
        using var temp = new TempWorkspace();
        var samples = new float[2000];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (i % 251) / 251f;
        }

        string path = temp.WriteWav("a.wav", samples);
        using WavPcmSource source = WavPcmSource.TryOpen(path)!;

        // A window in the middle, read on its own, must equal the same slice of
        // the whole file: the per-window conversion is where a stride or offset
        // mistake would show.
        float[] whole = source.ReadWindow(0, samples.Length);
        float[] slice = source.ReadWindow(500, 300);
        Assert.Equal(300, slice.Length);
        for (int i = 0; i < 300; i++)
        {
            Assert.Equal(whole[500 + i], slice[i], 6);
        }

        // Past the end clamps rather than throwing or returning junk.
        Assert.Empty(source.ReadWindow(samples.Length, 100));
        Assert.Equal(50, source.ReadWindow(samples.Length - 50, 500).Length);
    }

    [Fact]
    public void Load_WorksWithFfmpegAbsentFromPath()
    {
        // End to end, and with the tool genuinely unreachable: PATH is replaced so
        // ffmpeg cannot be found even if it is installed.
        using var temp = new TempWorkspace();
        string path = temp.WriteWav("a.wav", new float[1600]);

        string? original = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", temp.Path);
            _output.WriteLine($"PATH set to {temp.Path} (no ffmpeg there)");

            float[] pcm = AudioLoader.Load(path);

            Assert.Equal(1600, pcm.Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }

    private static void WriteWavWithFormat(string path, short bitsPerSample, int sampleRate, short channels)
    {
        const int frames = 100;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + (frames * channels * (bitsPerSample / 8)));
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * (bitsPerSample / 8));
        w.Write((short)(channels * (bitsPerSample / 8)));
        w.Write(bitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(frames * channels * (bitsPerSample / 8));
        for (int i = 0; i < frames * channels; i++)
        {
            if (bitsPerSample == 16)
            {
                w.Write((short)1000);
            }
            else
            {
                w.Write(new byte[] { 0, 0, 0xE8 });
            }
        }
    }

    private static void WriteWavWithLeadingListChunk(string path, float[] samples)
    {
        int dataBytes = samples.Length * 2;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes + 26);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(16_000);
        w.Write(32_000);
        w.Write((short)2);
        w.Write((short)16);

        // A chunk this reader does not know, of the kind ffmpeg emits.
        byte[] listBody = "INFOISFTLavf63.1.101"u8.ToArray();
        w.Write("LIST"u8.ToArray());
        w.Write(listBody.Length);
        w.Write(listBody);

        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        foreach (float s in samples)
        {
            w.Write((short)Math.Clamp(s * 32768f, short.MinValue, short.MaxValue));
        }
    }

    /// <summary>Keeps these tests off each other's PATH manipulation.</summary>
    [CollectionDefinition(nameof(DirectWavPathTests), DisableParallelization = true)]
    public class DirectWavPathTestGroup
    {
    }
}
