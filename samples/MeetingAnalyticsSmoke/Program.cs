namespace MeetingAnalyticsSmoke;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using Task = System.Threading.Tasks.Task;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.MeetingAnalytics.Voiceprint;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.Voiceprint;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || Has(args, "--help"))
        {
            Usage();
            return 0;
        }

        string? audio = Option(args, "--audio");
        string? asrModel = Option(args, "--asr-model");
        string? diarizerModel = Option(args, "--diarizer-model");
        string? speakerModel = Option(args, "--speaker-model");
        string? outDir = Option(args, "--out-dir");

        if (audio is null || asrModel is null || diarizerModel is null || speakerModel is null || outDir is null)
        {
            Usage();
            return 2;
        }

        try
        {
            await RunAsync(args, audio, asrModel, diarizerModel, speakerModel, outDir).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MeetingAnalyticsSmoke failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task RunAsync(
        string[] args,
        string audio,
        string asrModel,
        string diarizerModel,
        string speakerModel,
        string outDir)
    {
        Directory.CreateDirectory(outDir);

        float[] pcm = PcmExtensions.ReadWavToPcm(audio);
        TimeSpan duration = TimeSpan.FromSeconds(pcm.Length / 16000.0);
        string transcriptId = Path.GetFileNameWithoutExtension(audio);
        string language = Option(args, "--language") ?? string.Empty;
        string condition = Option(args, "--condition") ?? "clean";
        int numThreads = IntOption(args, "--num-threads", 1);
        double minScore = DoubleOption(args, "--min-score", 0.75);
        double minMargin = DoubleOption(args, "--min-margin", 0.10);
        int maxRechecks = IntOption(args, "--max-rechecks", 1);
        int maxSnippets = IntOption(args, "--max-snippets", 3);
        int minTurnMs = IntOption(args, "--min-turn-ms", 0);
        int paddingMs = IntOption(args, "--padding-ms", 0);
        int maxSnippetMs = IntOption(args, "--max-snippet-ms", 0);
        string modelId = Option(args, "--model-id") ?? "tidyvoicex_samresnet34@hr16-ONNX-sherpa-1.13.8";
        string? modelSha256 = Option(args, "--model-sha256");
        string voiceprintPath = Option(args, "--voiceprints") ?? Path.Combine(outDir, "voiceprints.json");
        string unknownPath = Option(args, "--unknowns") ?? Path.Combine(outDir, "unknown-speakers.json");

        Backends.InitDefault();

        Console.WriteLine($"audio: {audio} ({duration.TotalSeconds:0.0}s, {pcm.Length} samples)");
        Console.WriteLine("pass 1/3: transcription");
        Transcript transcript;
        using (Model model = Model.Load(modelPath: asrModel, configure: p => p.WithBackend(BackendRequest.BackendCpu)))
        using (Session session = model.CreateSession())
        {
            transcript = session.Run(pcm, r => r.WithDiarize(DiarizeMode.DiarizeModeOff));
        }

        Console.WriteLine("pass 2/3: diarization");
        IReadOnlyList<SpeakerSegmentResult> turns;
        using (Model model = Model.Load(diarizerModel, p => p.WithBackend(BackendRequest.BackendCpu)))
        using (DiarizeSession session = model.CreateDiarizeSession())
        using (var preset = new SortformerDiarizeExtBuilder().WithPreset(SortformerPreset.SortformerPresetDefault))
        {
            turns = session.Run(pcm, p => p.WithSortformerExt(preset));
        }

        List<MergedSegment> segments = transcript.Segments
            .Select(s => new MergedSegment(s.Start, s.End, s.Text, SpeakerId: 0))
            .ToList();
        List<MergedSegment> assigned = SpeakerAssignment.Apply(segments, turns, transcript.Words);

        var analyticsOptions = new MeetingAnalyticsOptions(
            RecordingDuration: duration,
            MinSilence: TimeSpan.FromSeconds(1),
            MinInterruptedSpeech: TimeSpan.FromMilliseconds(500),
            BackchannelMaxDuration: TimeSpan.FromMilliseconds(1500),
            MinOverlap: TimeSpan.Zero);
        MeetingAnalytics anonymous = MeetingAnalyzer.Analyze(turns, transcript.Words, analyticsOptions);

        Console.WriteLine("pass 3/3: optional voiceprint resolution");
        var voiceprintStore = new JsonVoiceprintStore(voiceprintPath);
        var unknownStore = new JsonUnknownSpeakerStore(unknownPath);
        using var embedder = new SherpaOnnxSpeakerEmbedder(speakerModel, modelId, numThreads, modelSha256);
        var resolutionOptions = new MeetingSpeakerResolutionOptions(
            IdentityPolicy: new AmbiguityPolicy(minScore, minMargin, maxRechecks),
            AnalyticsOptions: analyticsOptions,
            PersistUnknownSpeakers: true,
            MaxSnippetsPerSpeaker: maxSnippets,
            MinTurnMs: minTurnMs,
            PaddingMs: paddingMs,
            MaxSnippetMs: maxSnippetMs,
            ModelSha256: modelSha256);
        var resolver = new MeetingSpeakerResolver(embedder, voiceprintStore, unknownStore, resolutionOptions);
        MeetingSpeakerResolution resolved = await resolver.ResolveAsync(
            pcm,
            turns,
            transcript.Words,
            transcriptId,
            language,
            condition).ConfigureAwait(false);

        string jsonPath = Path.Combine(outDir, "analytics.json");
        string csvPath = Path.Combine(outDir, "analytics.csv");
        string markdownPath = Path.Combine(outDir, "analytics.md");
        string vttPath = Path.Combine(outDir, "transcript.vtt");

        await File.WriteAllTextAsync(jsonPath, MeetingAnalyticsJson.Write(resolved.Analytics)).ConfigureAwait(false);
        await File.WriteAllTextAsync(csvPath, MeetingAnalyticsCsv.Write(resolved.Analytics)).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, MeetingAnalyticsMarkdown.Write(resolved.Analytics)).ConfigureAwait(false);

        using (var writer = new StreamWriter(vttPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            SubtitleWriter.Write(
                writer,
                SubtitleFormat.Vtt,
                assigned.Select(s => new SubtitleCue(s.Start.TotalMilliseconds, s.End.TotalMilliseconds, s.SpeakerId, s.Text)),
                speakerLabels: true,
                title: transcriptId,
                speakerNames: resolved.SpeakerNames);
        }

        Console.WriteLine($"speakers: {resolved.Analytics.Summary.SpeakerCount}");
        Console.WriteLine($"known: {resolved.Analytics.SpeakerLabels.Count(l => l.Status == MeetingSpeakerIdentityStatus.Known)}");
        Console.WriteLine($"unknown: {resolved.Analytics.SpeakerLabels.Count(l => l.Status == MeetingSpeakerIdentityStatus.Unknown)}");
        Console.WriteLine($"ambiguous: {resolved.Analytics.SpeakerLabels.Count(l => l.Status == MeetingSpeakerIdentityStatus.Ambiguous)}");
        Console.WriteLine($"speech: {resolved.Analytics.Summary.TotalSpeechTime}");
        Console.WriteLine($"silence: {resolved.Analytics.Summary.TotalSilenceTime}");
        Console.WriteLine($"overlap: {resolved.Analytics.Summary.TotalOverlapTime}");
        Console.WriteLine($"outputs: {jsonPath}, {csvPath}, {markdownPath}, {vttPath}");
    }

    private static string? Option(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool Has(string[] args, string name) => args.Contains(name, StringComparer.Ordinal);

    private static int IntOption(string[] args, string name, int fallback)
        => int.TryParse(Option(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static double DoubleOption(string[] args, string name, double fallback)
        => double.TryParse(Option(args, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;

    private static void Usage()
    {
        Console.Error.WriteLine("MeetingAnalyticsSmoke --audio a.wav --asr-model asr.gguf --diarizer-model diar.gguf --speaker-model speaker.onnx --out-dir out");
        Console.Error.WriteLine("Options: --voiceprints file.json --unknowns file.json --model-id ID --model-sha256 SHA --num-threads N");
        Console.Error.WriteLine("         --min-score X --min-margin X --max-rechecks N --max-snippets N --min-turn-ms N --padding-ms N --max-snippet-ms N");
        Console.Error.WriteLine("WAV: 16-bit PCM, 16 kHz, mono/stereo. No raw transcript rewrite; transcript.vtt is a separate export.");
    }
}
