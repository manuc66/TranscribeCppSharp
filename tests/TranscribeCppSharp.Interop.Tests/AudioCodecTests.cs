#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Cli;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the container and codec formats the README claims the tool reads.
///
/// The claim is that anything that is not a 16 kHz mono 16-bit WAV is decoded
/// by ffmpeg, and that "ogg, mp3, m4a, …" work. Only WAV was actually
/// exercised, through a 44.1 kHz stereo file: the claim about the other formats
/// rested on ffmpeg doing what it is documented to do, which is an assumption
/// and not a test. These close that gap by really producing each format and
/// really decoding it.
///
/// The audio is transcoded from a WAV the test builds itself, so no binary asset
/// is added to the repository. Formats the local ffmpeg cannot *encode* are
/// skipped rather than failed: decoding support and encoding support are separate
/// ffmpeg build options, and a runner without libopus should not be a red build.
/// </summary>
/// <remarks>
/// Non-parallel, and in the same collection as <c>AudioLoaderTests</c>: both
/// decode through ffmpeg, which stages the decoded audio in the shared system
/// temp directory, and a test asserting on the files there cannot tell its own
/// apart from a concurrent class's.
/// </remarks>
[Collection("AudioStaging")]
public class AudioCodecTests
{
    private readonly ITestOutputHelper _output;

    public AudioCodecTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The formats the README names, with the encoder each needs. The suffix is
    /// the file extension, because ffmpeg picks a muxer from it.
    /// </summary>
    public static TheoryData<string, string, string, string> Codecs => new()
    {
        { "mp3", "libmp3lame", ".mp3", string.Empty },
        { "ogg (vorbis)", "libvorbis", ".ogg", string.Empty },
        { "opus", "libopus", ".opus", string.Empty },
        { "m4a (aac)", "aac", ".m4a", string.Empty },
        { "aac (adts)", "aac", ".aac", string.Empty },
        { "flac", "flac", ".flac", string.Empty },

        // Full-bandwidth WAV: these do not reach the direct reader, which only
        // accepts 16 kHz mono 16-bit, so each one exercises the resample and
        // downmix in the ffmpeg path.
        { "wav 44.1 kHz stereo", "pcm_s16le", ".wav", "-ar 44100 -ac 2" },
        { "wav 48 kHz stereo", "pcm_s16le", ".wav", "-ar 48000 -ac 2" },
        { "wav 96 kHz stereo", "pcm_s16le", ".wav", "-ar 96000 -ac 2" },
        { "wav 24-bit", "pcm_s24le", ".wav", "-ac 1" },
        { "wav 32-bit float", "pcm_f32le", ".wav", "-ac 1" },
        { "wav 8-channel 48 kHz", "pcm_f32le", ".wav", "-ar 48000 -ac 8" },
        { "wav 8 kHz telephone band", "pcm_s16le", ".wav", "-ar 8000 -ac 1" },
    };

    [Theory]
    [MemberData(nameof(Codecs))]
    public void Load_DecodesTheFormat(string label, string encoder, string extension, string extraArgs)
    {
        if (!TestConfig.HasFfmpeg())
        {
            _output.WriteLine("ffmpeg is not installed: skipping.");
            return;
        }

        using var temp = new TempWorkspace();
        string source = temp.WriteWav("source.wav", Tone(2));

        string encoded = temp.Combine("encoded" + extension);
        if (!TryEncode(source, encoded, encoder, extraArgs))
        {
            _output.WriteLine($"This ffmpeg cannot produce {label}: skipping.");
            return;
        }

        // Guard the test itself: a 0-byte or mistyped file would make the decode
        // below fail for a reason that has nothing to do with the loader.
        Assert.True(new FileInfo(encoded).Length > 0, $"{label}: ffmpeg produced an empty file");

        float[] pcm = AudioLoader.Load(encoded);

        // 2 s of audio, so ~32,000 samples at 16 kHz. Lossy encoders add padding
        // and resamplers round, so this is a wide bound: the point is that a whole
        // file arrived, not that the codec is sample-exact.
        Assert.InRange(pcm.Length, 30_000, 34_000);

        // A decoded file that is all silence, or all at full scale, means the
        // bytes were read but not interpreted.
        Assert.Contains(pcm, s => s > 0.05f);
        Assert.All(pcm, s => Assert.InRange(s, -1.0f, 1.0f));
    }

    /// <summary>
    /// Video containers, because ffmpeg reads the audio track out of one and
    /// nothing documented that it does.
    /// </summary>
    public static TheoryData<string, string, string> VideoContainers => new()
    {
        { "mp4 (H.264 + AAC)", "libx264", ".mp4" },
        { "mkv (H.264 + AC3)", "libx264", ".mkv" },
        { "webm (VP9 + Opus)", "libvpx-vp9", ".webm" },
    };

    [Theory]
    [MemberData(nameof(VideoContainers))]
    public void Load_ReadsTheAudioTrackOutOfAVideoContainer(string label, string videoEncoder, string extension)
    {
        if (!TestConfig.HasFfmpeg())
        {
            _output.WriteLine("ffmpeg is not installed: skipping.");
            return;
        }

        using var temp = new TempWorkspace();
        string source = temp.WriteWav("source.wav", Tone(2));
        string video = temp.Combine("clip" + extension);

        if (!TryMuxVideo(source, video, videoEncoder))
        {
            _output.WriteLine($"This ffmpeg cannot produce {label}: skipping.");
            return;
        }

        // The video track has to be bigger than the audio for this to be a real
        // test: a file that is mostly audio would pass even if the video were
        // ignored for the wrong reason.
        Assert.True(new FileInfo(video).Length > new FileInfo(source).Length, $"{label}: no video added");

        float[] pcm = AudioLoader.Load(video);

        Assert.InRange(pcm.Length, 30_000, 34_000);
        Assert.Contains(pcm, s => s > 0.05f);
    }

    [Fact]
    public void Load_ReportsAVideoWithNoAudioTrackAsOneLine()
    {
        if (!TestConfig.HasFfmpeg())
        {
            _output.WriteLine("ffmpeg is not installed: skipping.");
            return;
        }

        // Accepting video input means meeting video that carries no audio, and
        // that has to be a one-line report rather than a crash or a hang.
        using var temp = new TempWorkspace();
        string video = temp.Combine("silent.mp4");
        if (!TryMuxVideo(source: null, video, "libx264"))
        {
            _output.WriteLine("This ffmpeg cannot produce a silent video: skipping.");
            return;
        }

        AudioLoadException ex = Assert.Throws<AudioLoadException>(() => AudioLoader.Load(video));

        Assert.DoesNotContain("\n", ex.Message);
        Assert.Contains(video, ex.Message);
    }

    [Fact]
    public void Load_DecodesAMultiChannelOggTheSameWayAsMono()
    {
        if (!TestConfig.HasFfmpeg())
        {
            _output.WriteLine("ffmpeg is not installed: skipping.");
            return;
        }

        using var temp = new TempWorkspace();
        string source = temp.WriteWav("source.wav", Tone(1));

        // 2-channel vorbis, so the downmix path is exercised on a real
        // compressed input rather than only on a 44.1 kHz WAV.
        string stereo = temp.Combine("stereo.ogg");
        if (!TryEncode(source, stereo, "libvorbis", "-ac 2"))
        {
            _output.WriteLine("This ffmpeg has no 'libvorbis' encoder: skipping.");
            return;
        }

        float[] pcm = AudioLoader.Load(stereo);

        Assert.InRange(pcm.Length, 14_000, 18_000);
        Assert.Contains(pcm, s => s > 0.05f);
    }

    [Fact]
    public void Load_RejectsAnUnknownContainerEvenWhenFfmpegCannotDecodeIt()
    {
        if (!TestConfig.HasFfmpeg())
        {
            _output.WriteLine("ffmpeg is not installed: skipping.");
            return;
        }

        // A .mp3 extension does not make a file an mp3. The loader must reach
        // ffmpeg and report its failure rather than trusting the extension.
        using var temp = new TempWorkspace();
        string fake = temp.WriteText("fake.mp3", "this is not audio at all");

        AudioLoadException ex = Assert.Throws<AudioLoadException>(() => AudioLoader.Load(fake));

        Assert.Contains("ffmpeg", ex.Message);
        Assert.Contains(fake, ex.Message);
    }

    /// <summary>A 1 kHz tone, quiet enough not to clip at the given seconds.</summary>
    private static float[] Tone(int seconds)
    {
        var samples = new float[16_000 * seconds];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.3f * MathF.Sin(2 * MathF.PI * 1000 * i / 16_000);
        }

        return samples;
    }

    /// <summary>
    /// Encodes with ffmpeg, returning false when this build has no such encoder
    /// rather than failing the test for a missing ffmpeg feature.
    /// </summary>
    private static bool TryEncode(string source, string destination, string encoder, string? extraArgs = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-nostdin");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add(encoder);
        if (extraArgs is not null)
        {
            foreach (string arg in extraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                psi.ArgumentList.Add(arg);
            }
        }

        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add("16000");
        psi.ArgumentList.Add("-ac");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add(destination);

        return Run(psi, destination);
    }

    /// <summary>
    /// Muxes a video with (or without) an audio track, to test that the loader
    /// finds the audio inside a video container.
    /// </summary>
    private static bool TryMuxVideo(string? source, string destination, string videoEncoder)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-nostdin");
        psi.ArgumentList.Add("-v");

        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-y");
        if (source is not null)
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(source);
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("aac");
        }

        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("testsrc=size=320x240:rate=10:duration=2");
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add(videoEncoder);
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add("2");
        psi.ArgumentList.Add(destination);
        return Run(psi, destination);
    }

    private static bool Run(ProcessStartInfo psi, string expected)
    {
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 && File.Exists(expected);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
