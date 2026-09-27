#nullable enable

using System;
using System.IO;
using System.Linq;
using TranscribeCppSharp.Cli;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for audio loading. The contract that matters for a first run: a file
/// the tool cannot read produces a one-line AudioLoadException, never a raw
/// .NET exception that reaches the console as a stack trace (exit code 134).
/// </summary>
public class AudioLoaderTests
{
    private readonly ITestOutputHelper _output;

    public AudioLoaderTests(ITestOutputHelper output) => _output = output;

    private static bool HasFfmpeg => ResolveFfmpeg() is not null;

    private static string? ResolveFfmpeg()
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);
    }

    [Fact]
    public void Load_ReadsA16kHzMonoWav()
    {
        using var temp = new TempWorkspace();
        var samples = new float[1600];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (i % 100) / 100f;
        }

        string wav = temp.WriteWav("a.wav", samples);
        float[] pcm = AudioLoader.Load(wav);

        Assert.Equal(samples.Length, pcm.Length);
        Assert.Equal(0f, pcm[0], 3);
        Assert.Equal(0.99f, pcm[99], 2);
    }

    [Fact]
    public void Load_RejectsAFileThatIsTooShortToBeAWav()
    {
        // Used to escape as EndOfStreamException from the WAV reader and crash
        // the tool with a stack trace.
        using var temp = new TempWorkspace();
        string junk = temp.WriteBytes("tiny.bin", [1, 2, 3, 4, 5]);

        AudioLoadException ex = Assert.Throws<AudioLoadException>(() => AudioLoader.Load(junk));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Load_RejectsTextNamedLikeAudio()
    {
        using var temp = new TempWorkspace();
        string notAudio = temp.WriteText("notaudio.wav", "this is not audio");

        AudioLoadException ex = Assert.Throws<AudioLoadException>(() => AudioLoader.Load(notAudio));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Load_RejectsATruncatedWavHeader()
    {
        using var temp = new TempWorkspace();
        // "RIFF" + a size, then nothing: the reader runs off the end of the file.
        string truncated = temp.WriteBytes("truncated.wav", [0x52, 0x49, 0x46, 0x46, 0x24, 0x00]);

        Assert.ThrowsAny<Exception>(() => AudioLoader.Load(truncated));
    }

    [Fact]
    public void Load_DecodesANonWavFormatWithFfmpeg()
    {
        if (!HasFfmpeg)
        {
            _output.WriteLine("ffmpeg is not installed: skipping the ffmpeg fallback.");
            return;
        }

        using var temp = new TempWorkspace();
        // A 44.1 kHz stereo WAV is not read directly (the native layer wants
        // 16 kHz mono); ffmpeg converts it.
        const int frames = 800;
        string source = temp.Combine("source.wav");
        WriteWav44kStereo(source, frames);

        float[] pcm = AudioLoader.Load(source);

        // Resampled from 44.1 kHz to 16 kHz, stereo downmixed to mono.
        Assert.Equal(frames * 16000 / 44100, pcm.Length);
    }

    [Fact]
    public void Load_WhenFfmpegCannotDecode_ExplainsBothWaysOut()
    {
        if (!HasFfmpeg)
        {
            _output.WriteLine("ffmpeg is not installed: skipping.");
            return;
        }

        using var temp = new TempWorkspace();
        string notAudio = temp.WriteText("notaudio.wav", "this is not audio");

        AudioLoadException ex = Assert.Throws<AudioLoadException>(() => AudioLoader.Load(notAudio));

        Assert.Contains("16 kHz mono 16-bit WAV", ex.Message);
        Assert.Contains("ffmpeg", ex.Message);
        Assert.Contains(notAudio, ex.Message);
    }

    private static void WriteWav44kStereo(string path, int frames)
    {
        const int sampleRate = 44_100;
        const short channels = 2;
        const short bits = 16;
        int dataBytes = frames * channels * (bits / 8);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * (bits / 8));
        w.Write((short)(channels * (bits / 8)));
        w.Write(bits);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        for (int i = 0; i < frames * channels; i++)
        {
            w.Write((short)0);
        }
    }
}
