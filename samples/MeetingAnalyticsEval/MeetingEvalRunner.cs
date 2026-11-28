namespace MeetingAnalyticsEval;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.MeetingAnalytics.Voiceprint;
using TranscribeCppSharp.Voiceprint;

/// <summary>Runs the real ASR/diarization/voiceprint pipeline over an annotated corpus.</summary>
public sealed class MeetingEvalRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly string asrModelPath;
    private readonly string diarizerModelPath;
    private readonly string speakerModelPath;
    private readonly string modelId;
    private readonly string? modelSha256;
    private readonly int numThreads;
    private readonly string voiceprintPath;
    private readonly string unknownPath;
    private readonly AmbiguityPolicy identityPolicy;
    private readonly MeetingSpeakerResolutionOptions resolutionOptions;

    public MeetingEvalRunner(
        string asrModelPath,
        string diarizerModelPath,
        string speakerModelPath,
        string modelId,
        string? modelSha256,
        int numThreads,
        string voiceprintPath,
        string unknownPath,
        AmbiguityPolicy identityPolicy,
        MeetingSpeakerResolutionOptions resolutionOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asrModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(diarizerModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numThreads);
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceprintPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(unknownPath);
        ArgumentNullException.ThrowIfNull(identityPolicy);
        ArgumentNullException.ThrowIfNull(resolutionOptions);

        this.asrModelPath = asrModelPath;
        this.diarizerModelPath = diarizerModelPath;
        this.speakerModelPath = speakerModelPath;
        this.modelId = modelId;
        this.modelSha256 = modelSha256;
        this.numThreads = numThreads;
        this.voiceprintPath = voiceprintPath;
        this.unknownPath = unknownPath;
        this.identityPolicy = identityPolicy;
        this.resolutionOptions = resolutionOptions;
    }

    public async ValueTask<MeetingEvalResults> RunAsync(string corpusPath, string outDir, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outDir);
        if (!File.Exists(corpusPath))
        {
            throw new FileNotFoundException("Corpus manifest not found.", corpusPath);
        }

        Directory.CreateDirectory(outDir);
        string json = await File.ReadAllTextAsync(corpusPath, cancellationToken).ConfigureAwait(false);
        MeetingEvalCorpus corpus = JsonSerializer.Deserialize<MeetingEvalCorpus>(json, JsonOptions) ?? new MeetingEvalCorpus();
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(corpusPath)) ?? Directory.GetCurrentDirectory();

        Backends.InitDefault();
        var voiceprintStore = new JsonVoiceprintStore(voiceprintPath);
        var unknownStore = new JsonUnknownSpeakerStore(unknownPath);

        using Model asrModel = Model.Load(asrModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using Model diarizerModel = Model.Load(diarizerModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var embedder = new SherpaOnnxSpeakerEmbedder(speakerModelPath, modelId, numThreads, modelSha256);
        var resolver = new MeetingSpeakerResolver(embedder, voiceprintStore, unknownStore, resolutionOptions);
        var meetings = new List<MeetingEvalResult>();

        for (int i = 0; i < corpus.Meetings.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MeetingEvalCase meeting = corpus.Meetings[i];
            string audio = Path.GetFullPath(Path.Combine(baseDirectory, meeting.Audio));
            float[] pcm = PcmExtensions.ReadWavToPcm(audio);
            TimeSpan duration = TimeSpan.FromSeconds(pcm.Length / 16000.0);
            string meetingId = $"meeting-{i + 1}-{Path.GetFileNameWithoutExtension(audio)}";

            Console.WriteLine($"[{i + 1}/{corpus.Meetings.Count}] {meetingId} ({duration.TotalSeconds:0.0}s)");
            Transcript transcript;
            using (Session session = asrModel.CreateSession())
            {
                transcript = session.Run(pcm, r => r.WithDiarize(DiarizeMode.DiarizeModeOff), cancellationToken);
            }

            IReadOnlyList<SpeakerSegmentResult> turns;
            using (DiarizeSession session = diarizerModel.CreateDiarizeSession())
            using (var preset = new SortformerDiarizeExtBuilder().WithPreset(SortformerPreset.SortformerPresetDefault))
            {
                turns = session.Run(pcm, p => p.WithSortformerExt(preset), cancellationToken);
            }

            MeetingAnalytics analytics = MeetingAnalyzer.Analyze(turns, transcript.Words, resolutionOptions.AnalyticsOptions);
            Process process = Process.GetCurrentProcess();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            var stopwatch = Stopwatch.StartNew();
            MeetingSpeakerResolution resolved = await resolver.ResolveAsync(
                pcm,
                turns,
                transcript.Words,
                meetingId,
                meeting.Language,
                meeting.Condition,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            double resolveCpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;

            meetings.Add(MeetingEvalEvaluator.Evaluate(
                meeting,
                meetingId,
                duration,
                resolved.Analytics,
                resolved,
                stopwatch.Elapsed.TotalMilliseconds,
                resolveCpu));
        }

        MeetingEvalSummary summary = MeetingEvalEvaluator.Summarize(meetings);
        MeetingEvalCalibration calibration = MeetingEvalCalibrator.Calibrate(meetings.SelectMany(m => m.Speakers).ToList());
        var results = new MeetingEvalResults(summary, meetings);

        await File.WriteAllTextAsync(Path.Combine(outDir, "results.json"), JsonSerializer.Serialize(results, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDir, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDir, "thresholds.json"), JsonSerializer.Serialize(calibration, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDir, "summary.csv"), MeetingEvalCsv.Write(summary), cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"meetings={summary.TotalMeetings} speakers={summary.TotalSpeakers} evaluated={summary.EvaluatedSpeakers}");
        Console.WriteLine($"FAR={summary.Far:0.0000} FRR={summary.Frr:0.0000} unknownRejection={summary.UnknownRejectionRate:0.0000}");
        Console.WriteLine($"best MinScore={calibration.Best.MinScore:0.000} MinMargin={calibration.Best.MinMargin:0.000}");
        Console.WriteLine($"outputs: {outDir}");
        return results;
    }
}

/// <summary>CSV summary exporter.</summary>
public static class MeetingEvalCsv
{
    public static string Write(MeetingEvalSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Meetings,Speakers,Evaluated,Target,NonTarget,Unknown,PersonCorrect,TrueAccept,FalseAccept,FalseReject,TrueReject,UnknownRejected,Ambiguous,FAR,FRR,UnknownRejectionRate,PersonCorrectRate,Accuracy,BalancedErrorRate,Coverage,AudioSeconds,ResolveWallMs,ResolveCpuMs,ResolveCpuMsPerAudioSecond");
        builder.Append(summary.TotalMeetings.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.TotalSpeakers.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.EvaluatedSpeakers.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.TargetCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.NonTargetCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.UnknownCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.PersonCorrectCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.TrueAccept.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.FalseAccept.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.FalseReject.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.TrueReject.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.UnknownRejected.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.AmbiguousCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.Far.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.Frr.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.UnknownRejectionRate.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.PersonCorrectRate.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.Accuracy.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.BalancedErrorRate.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.Coverage.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.AudioSecondsTotal.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.ResolveWallMsTotal.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.ResolveCpuMsTotal.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.ResolveCpuMsPerAudioSecond.ToString("0.###", CultureInfo.InvariantCulture))
            .AppendLine();
        return builder.ToString();
    }
}
