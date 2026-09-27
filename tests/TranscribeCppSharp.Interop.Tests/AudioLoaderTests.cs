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

    [Fact]
    public void Load_FfmpegPathReadsEveryByteOfALargeStream()
    {
        if (!HasFfmpeg)
        {
            _output.WriteLine("ffmpeg is not installed: skipping the ffmpeg fallback.");
            return;
        }

        using var temp = new TempWorkspace();

        // A 16 kHz mono WAV, so the direct reader handles it. Sent through
        // ffmpeg explicitly (rather than via Load, which short-circuits), the
        // decode path has to reproduce the WAV reader's floats exactly, which
        // pins the f32 byte order and the 1/32768 scaling.
        //
        // The signal is 20 s long, so 1.28 MB of f32 crosses the read buffer and
        // the ReadExactly path that fills the float[] is genuinely exercised.
        const int frames = 20 * 16_000;
        var expected = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            // A ramp plus a step, so a misaligned read shows up as a shifted or
            // wrapped value rather than a plausible-looking constant.
            expected[i] = ((i % 512) - 256) / 300f;
        }

        string path = temp.WriteWav("mono16k-long.wav", expected);
        float[] viaDecode = AudioLoader.DecodeWithFfmpeg(path);

        Assert.Equal(expected.Length, viaDecode.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            // 3 decimals, not 4: the WAV on disk is 16-bit, so the value that
            // went in was already rounded to a multiple of 1/32768 (~3e-5) and
            // ffmpeg returns that rounded value as f32. Comparing tighter than
            // the source resolution would be comparing the rounding, not the
            // byte order.
            Assert.Equal(expected[i], viaDecode[i], 3);
        }
    }

    [Fact]
    public void Load_ResamplesAndDownmixesThroughFfmpeg()
    {
        if (!HasFfmpeg)
        {
            _output.WriteLine("ffmpeg is not installed: skipping the ffmpeg fallback.");
            return;
        }

        using var temp = new TempWorkspace();
        const int frames = 8000;
        string source = temp.Combine("stereo44k.wav");
        WriteConstantWav44kStereo(source, frames, sampleRate: 44_100, value: 0.5f);

        float[] pcm = AudioLoader.Load(source);

        // 8000 frames at 44.1 kHz is 0.18 s, so ~2903 samples at 16 kHz. The
        // exact count is ffmpeg's to decide, so this only pins two things: the
        // downmix and resample kept a non-zero signal everywhere (a short read
        // or a partially filled buffer would leave a run of zeros), and it
        // stayed flat (a byte-order or stride slip would not).
        //
        // The absolute level is deliberately not asserted: ffmpeg's resampler
        // does not have unity DC gain. Verified on this machine, a constant 0.5
        // input decodes to a constant 0.707, so pinning 0.5 would be pinning
        // ffmpeg's filter, which is not this code's business and can change
        // with an ffmpeg version.
        Assert.InRange(pcm.Length, 2800, 3000);
        Assert.All(pcm, s => Assert.NotEqual(0f, s));

        float first = pcm[0];
        Assert.All(pcm, s => Assert.Equal(first, s, 4));
        Assert.InRange(first, 0.4f, 0.9f);
    }

    [Fact]
    public void Load_FfmpegPathDoesNotBlockOnAnUnreadPipe()
    {
        if (!HasFfmpeg)
        {
            _output.WriteLine("ffmpeg is not installed: skipping the ffmpeg fallback.");
            return;
        }

        // The ffmpeg output goes to a file, so nothing reads the child's stdout.
        // A RedirectStandardOutput left over from when the output was a pipe
        // would create a pipe nobody drains, and a single byte would fill it and
        // hang the process. This asserts the call returns at all, and with the
        // right samples, on input large enough that a stall would show.
        using var temp = new TempWorkspace();
        const int frames = 44_100 * 60; // 60 s at 44.1 kHz, mono
        string source = temp.Combine("minute.wav");
        WriteRampWav44k1Mono(source, frames);

        // A generous ceiling: the decode of 60 s takes well under a second, so
        // anything near this is a stall. xUnit has no per-test timeout that
        // fails cleanly, so a hang shows as a suite timeout instead.
        var task = System.Threading.Tasks.Task.Run(() => AudioLoader.DecodeWithFfmpeg(source));
        Assert.True(
            task.Wait(TimeSpan.FromSeconds(60)),
            "DecodeWithFfmpeg did not return within 60 s: it is blocked on a stream nobody reads");

        float[] pcm = task.Result;
        long expectedSamples = (long)frames * 16000 / 44100;
        Assert.InRange(pcm.Length, (int)(expectedSamples - 64), (int)(expectedSamples + 64));
    }

    [Fact]
    public void Load_LeavesNoTemporaryFileBehind()
    {
        if (!HasFfmpeg)
        {
            _output.WriteLine("ffmpeg is not installed: skipping the ffmpeg fallback.");
            return;
        }

        using var temp = new TempWorkspace();
        string source = temp.Combine("stereo.wav");
        WriteConstantWav44kStereo(source, frames: 1000, sampleRate: 44_100, value: 0.25f);

        string tempRoot = Path.GetTempPath();
        string[] before = Directory.GetFiles(tempRoot, "transcribe-*.f32");
        _ = AudioLoader.Load(source);
        string[] after = Directory.GetFiles(tempRoot, "transcribe-*.f32");

        Assert.Equal(before.Length, after.Length);
    }

    [Fact]
    public void Load_ReportsOneLineWhenTheInputCannotBeDecoded()
    {
        if (!HasFfmpeg)
        {
            _output.WriteLine("ffmpeg is not installed: skipping.");
            return;
        }

        // A file with a valid WAV header shape but garbage payload: ffmpeg runs
        // and fails, so this covers the exit-code branch of the decode path.
        using var temp = new TempWorkspace();
        var junk = new byte[4096];
        junk.AsSpan().Fill(0xAB);
        string path = temp.WriteBytes("garbage.wav", junk);

        AudioLoadException ex = Assert.Throws<AudioLoadException>(() => AudioLoader.Load(path));

        Assert.DoesNotContain("\n", ex.Message);
        Assert.Contains(path, ex.Message);
    }

    private static void WriteRampWav44k1Mono(string path, int frames)
    {
        const int sampleRate = 44_100;
        const short channels = 1;
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

        var block = new byte[65536];
        int remaining = dataBytes;
        int frame = 0;
        while (remaining > 0)
        {
            int n = Math.Min(block.Length, remaining);
            for (int i = 0; i < n; i += 2)
            {
                short value = (short)(((frame++ % 200) - 100) * 300);
                block[i] = (byte)(value & 0xFF);
                block[i + 1] = (byte)((value >> 8) & 0xFF);
            }

            w.Write(block, 0, n);
            remaining -= n;
        }
    }

    private static void WriteConstantWav44kStereo(string path, int frames, int sampleRate, float value)
    {
        short bits = 16;
        short channels = 2;
        int dataBytes = frames * channels * (bits / 8);
        short sample = (short)Math.Clamp(value * short.MaxValue, short.MinValue, short.MaxValue);

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

        // One buffer for the whole payload: a per-sample write here would make
        // the test itself the slow part.
        var block = new byte[65536];
        for (int i = 0; i < block.Length; i += 2)
        {
            block[i] = (byte)(sample & 0xFF);
            block[i + 1] = (byte)((sample >> 8) & 0xFF);
        }

        int remaining = dataBytes;
        while (remaining > 0)
        {
            int n = Math.Min(block.Length, remaining);
            w.Write(block, 0, n);
            remaining -= n;
        }
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
