#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.Interop;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Speaker diarization, exercised through every entry point the wrapper offers.
/// The upstream semantics come from transcribe_diarize_mode in
/// ffi/c/transcribe.h: DEFAULT is the library default (OFF for every family),
/// ON requests and parses speaker markers into segment speaker_id + the
/// speaker-segment accessors, and a non-DEFAULT value against a model without
/// TRANSCRIBE_FEATURE_DIARIZATION warns and keeps the default behavior.
///
/// The tests that need a diarization-capable model use the MOSS Q4_K_M asset
/// (opt-in, ~617 MB: WITH_DIARIZATION_MODEL=1 ./scripts/run-integration-tests.sh)
/// and skip when it is absent. Whisper needs only test-models/ggml-tiny.bin.
/// </summary>
public class DiarizationTests
{
    private readonly ITestOutputHelper _output;

    public DiarizationTests(ITestOutputHelper output) => _output = output;

    private static string MossPath => Path.Combine(TestConfig.RepoRoot, "test-models", "moss-transcribe-diarize-q4-k-m.gguf");

    private static bool HasMoss => File.Exists(MossPath);

    private static bool HasWhisper => File.Exists(TestConfig.ModelPath);

    private static float[] Pcm() => PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

    /// <summary>Two voices: the sample, then a copy of it resampled 15% up.</summary>
    private static float[] TwoVoicePcm()
    {
        float[] one = Pcm();
        // Linear resample 16 kHz -> 18.4 kHz and back: same words, different
        // pitch, which is what a second voice is to a diarization model.
        const double factor = 18_400d / 16_000d;
        int length = (int)(one.Length / factor);
        var shifted = new float[length];
        for (int i = 0; i < length; i++)
        {
            double position = i * factor;
            int index = (int)position;
            double frac = position - index;
            shifted[i] = (float)(one[index] * (1 - frac) + one[Math.Min(index + 1, one.Length - 1)] * frac);
        }

        return one.Concat(shifted).ToArray();
    }

    // ---- the ways diarization can be asked for -------------------------------

    [Fact]
    public void DiarizationIsARunTimeToggleOnly_NotOnTheStreamOrTheSession()
    {
        // Documented limit, asserted so it is noticed if the upstream surface
        // ever gains a stream-level toggle: transcribe_stream_params has no
        // diarize field, so speaker attribution is not available through the
        // streaming API in transcribe.cpp v0.2.4.
        Assert.Null(BuilderAcceptingDiarize<StreamParamsBuilder>());
        Assert.Null(BuilderAcceptingDiarize<SessionParamsBuilder>());
        Assert.NotNull(BuilderAcceptingDiarize<RunParamsBuilder>());
    }

    [SkippableFact]
    public void Whisper_DoesNotReportDiarizationSupport_AndRunKeepsEverySegmentOnSpeakerZero()
    {
        Skip.IfNot(HasWhisper && File.Exists(TestConfig.AudioPath), "Whisper test asset not present.");

        using var model = Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.False(model.Supports(Feature.FeatureDiarization));

        using var session = model.CreateSession();
        float[] pcm = Pcm();
        var on = session.Run(pcm, r => r.WithDiarize(DiarizeMode.DiarizeModeOn));
        var off = session.Run(pcm, r => r.WithDiarize(DiarizeMode.DiarizeModeOff));
        var byDefault = session.Run(pcm);

        // Asked for and refused: the run still succeeds, with no attribution.
        Assert.Equal(off.FullText, on.FullText);
        Assert.Equal(byDefault.FullText, on.FullText);
        Assert.Empty(on.SpeakerSegments);
        Assert.All(on.Segments, s => Assert.Equal(0, s.SpeakerId));
    }

    [SkippableFact]
    public void Moss_ReportsDiarizationSupport_AndRunOnAttributesSpeakers()
    {
        Skip.IfNot(HasMoss && File.Exists(TestConfig.AudioPath), $"Diarization model not present at {MossPath}.");

        using var model = Model.Load(MossPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.True(model.Supports(Feature.FeatureDiarization));

        using var session = model.CreateSession();
        Transcript transcript = session.Run(Pcm(), r => r.WithDiarize(DiarizeMode.DiarizeModeOn));

        _output.WriteLine($"full: {transcript.FullText}");
        _output.WriteLine($"raw : {transcript.RawText}");
        Assert.NotEmpty(transcript.SpeakerSegments);
        Assert.All(transcript.SpeakerSegments, s => Assert.Equal(1, s.SpeakerId));
        Assert.All(transcript.Segments, s => Assert.Equal(1, s.SpeakerId));
        // The markers stay in the raw decode and are stripped from the text.
        Assert.Matches(@"\[S[0-9]{2}\]", transcript.RawText);
        Assert.DoesNotContain("[S01]", transcript.FullText);
    }

    [SkippableFact]
    public void Moss_WithDiarizeOff_GivesNoSpeakerAttributionButTheSameWords()
    {
        Skip.IfNot(HasMoss && File.Exists(TestConfig.AudioPath), $"Diarization model not present at {MossPath}.");

        using var model = Model.Load(MossPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        float[] pcm = Pcm();

        var off = session.Run(pcm, r => r.WithDiarize(DiarizeMode.DiarizeModeOff));
        var on = session.Run(pcm, r => r.WithDiarize(DiarizeMode.DiarizeModeOn));

        // OFF still strips the inline metadata, so the text is the same; only the
        // attribution disappears.
        Assert.Equal(on.FullText, off.FullText);
        Assert.Empty(off.SpeakerSegments);
        Assert.All(off.Segments, s => Assert.Equal(0, s.SpeakerId));
    }

    [SkippableFact]
    public void Moss_BatchRun_CarriesSpeakerSegmentsToo()
    {
        Skip.IfNot(HasMoss && File.Exists(TestConfig.AudioPath), $"Diarization model not present at {MossPath}.");

        using var model = Model.Load(MossPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        float[] pcm = Pcm();

        // Batch takes the same run-params callback, so diarization is a way
        // there too.
        IReadOnlyList<BatchResult> results = Batch.Run(session, new[] { pcm, pcm }, r => r.WithDiarize(DiarizeMode.DiarizeModeOn));

        Assert.Equal(2, results.Count);
        foreach (BatchResult result in results)
        {
            _output.WriteLine($"[{result.Index}] {result.FullText}");
            Assert.NotEmpty(result.SpeakerSegments);
            Assert.All(result.SpeakerSegments, s => Assert.Equal(1, s.SpeakerId));
            Assert.All(result.Segments, s => Assert.Equal(1, s.SpeakerId));
        }
    }

    [SkippableFact]
    public void Moss_TwoVoices_AreAttributedToTwoSpeakers()
    {
        Skip.IfNot(HasMoss && File.Exists(TestConfig.AudioPath), $"Diarization model not present at {MossPath}.");

        using var model = Model.Load(MossPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();

        Transcript transcript = session.Run(TwoVoicePcm(), r => r.WithDiarize(DiarizeMode.DiarizeModeOn));
        _output.WriteLine($"raw: {transcript.RawText}");

        int[] speakers = transcript.SpeakerSegments.Select(s => s.SpeakerId).Distinct().Order().ToArray();
        Assert.Equal([1, 2], speakers);
    }

    // ---- the ways the exported transcript carries the attribution ------------

    [SkippableFact]
    public void Moss_AllThreeExportFormatsCarryTheSpeaker()
    {
        Skip.IfNot(HasMoss && File.Exists(TestConfig.AudioPath), $"Diarization model not present at {MossPath}.");

        using var temp = new TempWorkspace();
        // Each format spells the attribution differently; all three carry it.
        var expectations = new (TranscriptFormat Format, string Marker)[]
        {
            (TranscriptFormat.Plain, "Speaker 1: "),
            (TranscriptFormat.Vtt, "<v Speaker 1>"),
            (TranscriptFormat.Json, "\"speaker\":1"),
        };

        foreach ((TranscriptFormat format, string marker) in expectations)
        {
            string outFile = temp.Combine($"{format}.out");
            (int exitCode, string stdout, _) = RunCli(TestConfig.AudioPath, MossPath, "--backend", "cpu", "--out", outFile, "--format", CliFormatName(format));
            _output.WriteLine(stdout);

            Assert.Equal(0, exitCode);
            Assert.Contains("diarization supported: True", stdout);
            Assert.Contains(marker, File.ReadAllText(outFile));
        }
    }

    [SkippableFact]
    public void Moss_NoDiarize_ExportsWithoutSpeakerAttribution()
    {
        Skip.IfNot(HasMoss && File.Exists(TestConfig.AudioPath), $"Diarization model not present at {MossPath}.");

        using var temp = new TempWorkspace();
        string outFile = temp.Combine("plain.out");
        (int exitCode, string stdout, _) = RunCli(TestConfig.AudioPath, MossPath, "--backend", "cpu", "--no-diarize", "--out", outFile);
        _output.WriteLine(stdout);

        Assert.Equal(0, exitCode);
        // The model still supports diarization; the run simply did not ask for it.
        Assert.Contains("diarization supported: True", stdout);
        string content = File.ReadAllText(outFile);
        Assert.Contains("# lang     : en   diarize: off", content);
        Assert.Matches(@"\[00:\d\d\.\d -> 00:\d\d\.\d\] Speaker 0: ", content);
    }

    // ---- helpers -------------------------------------------------------------

    private static MethodInfo? BuilderAcceptingDiarize<T>() =>
        typeof(T)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.GetParameters().Any(p => p.ParameterType == typeof(DiarizeMode)));

    private static string CliFormatName(TranscriptFormat format) => format switch
    {
        TranscriptFormat.Vtt => "vtt",
        TranscriptFormat.Json => "json",
        _ => "plain",
    };

    /// <summary>
    /// The CLI is driven through the process here, not through Run, because the
    /// wrapper types the export format is internal to the CLI.
    /// </summary>
    private static (int ExitCode, string Out, string Error) RunCli(params string[] args)
    {
        var stdout = new System.IO.StringWriter();
        var stderr = new System.IO.StringWriter();
        int exitCode = Cli.TranscribeCommand.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
