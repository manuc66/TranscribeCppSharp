namespace VoiceprintEval;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp;
using TranscribeCppSharp.Voiceprint;

/// <summary>Local eval: no cloud, no diarization, no recheck, no speaker naming.</summary>
public sealed class VoiceprintEvalRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private readonly string modelPath;
    private readonly string modelId;
    private readonly int numThreads;

    public VoiceprintEvalRunner(string modelPath, string modelId = "wespeaker_en_voxceleb_resnet34_LM@sherpa-onnx-1.13.8", int numThreads = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numThreads);

        this.modelPath = modelPath;
        this.modelId = modelId;
        this.numThreads = numThreads;
    }

    public async Task<VoiceprintEvalResults> RunAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("Speaker embedding model not found.", modelPath);
        }

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Manifest not found.", manifestPath);
        }

        string json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        VoiceprintEvalCorpus corpus = JsonSerializer.Deserialize<VoiceprintEvalCorpus>(json, JsonOptions) ?? new VoiceprintEvalCorpus();
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();

        using var embedder = new SherpaOnnxSpeakerEmbedder(modelPath, modelId, numThreads);
        var voiceprints = new List<Voiceprint>();

        foreach (VoiceprintEvalPerson person in corpus.Persons)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(person.Id);
            if (person.Enrollment.Count == 0)
            {
                throw new InvalidOperationException($"Person '{person.Id}' has no enrollment audio.");
            }

            var embeddings = new List<float[]>();
            foreach (string enrollment in person.Enrollment)
            {
                string path = ResolvePath(baseDirectory, enrollment);
                float[] pcm = ReadPcm16kMono(path);
                embeddings.Add(await embedder.EmbedAsync(pcm, cancellationToken).ConfigureAwait(false));
            }

            voiceprints.Add(new Voiceprint(
                person.Id,
                string.IsNullOrWhiteSpace(person.DisplayName) ? person.Id : person.DisplayName,
                modelId,
                embedder.Dimension,
                embeddings,
                DateTimeOffset.UtcNow,
                embeddings.Count));
        }

        var results = new List<VoiceprintEvalResult>();
        var rawPolicy = new AmbiguityPolicy(MinScore: -1.0, MinMargin: 0.0, MaxRechecks: 0);

        foreach (VoiceprintEvalPerson person in corpus.Persons)
        {
            foreach (string test in person.Test)
            {
                results.Add(await EvaluateOneAsync(embedder, voiceprints, rawPolicy, ResolvePath(baseDirectory, test), person.Id, person.Enrollment.Count, person.Language, person.Condition, cancellationToken).ConfigureAwait(false));
            }
        }

        foreach (string unknown in corpus.Unknown)
        {
            results.Add(await EvaluateOneAsync(embedder, voiceprints, rawPolicy, ResolvePath(baseDirectory, unknown), expectedPersonId: null, enrollmentCount: 0, language: string.Empty, condition: string.Empty, cancellationToken).ConfigureAwait(false));
        }

        foreach (VoiceprintEvalUnknownCase unknown in corpus.UnknownCases)
        {
            results.Add(await EvaluateOneAsync(embedder, voiceprints, rawPolicy, ResolvePath(baseDirectory, unknown.Path), expectedPersonId: null, enrollmentCount: 0, unknown.Language, unknown.Condition, cancellationToken).ConfigureAwait(false));
        }

        return new VoiceprintEvalResults(modelId, Summarize(results), results);
    }

    private async Task<VoiceprintEvalResult> EvaluateOneAsync(
        SherpaOnnxSpeakerEmbedder embedder,
        IReadOnlyList<Voiceprint> voiceprints,
        AmbiguityPolicy rawPolicy,
        string testPath,
        string? expectedPersonId,
        int enrollmentCount,
        string language,
        string condition,
        CancellationToken cancellationToken)
    {
        int pcmSamples = 0;
        double durationMs = 0.0;

        try
        {
            float[] pcm = ReadPcm16kMono(testPath);
            pcmSamples = pcm.Length;
            durationMs = pcm.Length / (embedder.SampleRate / 1000.0);

            Process process = Process.GetCurrentProcess();
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            TimeSpan cpuBefore = process.TotalProcessorTime;
            var stopwatch = Stopwatch.StartNew();

            float[] embedding = await embedder.EmbedAsync(pcm, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            double embeddingCpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            SpeakerMatch match = VoiceprintMatcher.Match(embedding, modelId, voiceprints, rawPolicy);
            string? actual = match.PersonId;
            bool hasCandidate = actual is not null;
            bool personCorrect = expectedPersonId is not null && string.Equals(expectedPersonId, actual, StringComparison.Ordinal);

            return new VoiceprintEvalResult(
                testPath,
                expectedPersonId,
                actual,
                personCorrect,
                hasCandidate,
                IsFinite(embedding),
                modelId,
                embedding.Length,
                pcmSamples,
                durationMs,
                match.BestScore,
                match.SecondBestScore,
                match.Margin,
                stopwatch.Elapsed.TotalMilliseconds,
                embeddingCpuMs,
                allocatedBytes,
                enrollmentCount,
                language,
                condition);
        }
        catch (InvalidDataException)
        {
        }
        catch (FileNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        // Unreadable/too-short/non-finite/unsupported audio becomes a non-match row; calibration excludes Finite=false.
        return new VoiceprintEvalResult(
            testPath,
            expectedPersonId,
            ActualPersonId: null,
            PersonCorrect: false,
            HasCandidate: false,
            Finite: false,
            modelId,
            Dimension: 0,
            pcmSamples,
            durationMs,
            BestScore: double.NaN,
            SecondBestScore: double.NaN,
            Margin: double.NaN,
            EmbeddingWallMs: 0.0,
            EmbeddingCpuMs: 0.0,
            EmbeddingAllocatedBytes: 0,
            enrollmentCount,
            language,
            condition);
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static float[] ReadPcm16kMono(string path)
    {
        // PcmExtensions reads 16-bit PCM WAV at 16 kHz; stereo is downmixed. No resampling here.
        return PcmExtensions.ReadWavToPcm(path);
    }

    private static bool IsFinite(float[] values)
    {
        foreach (float value in values)
        {
            if (!float.IsFinite(value))
            {
                return false;
            }
        }

        return values.Length > 0;
    }

    private static VoiceprintEvalSummary Summarize(List<VoiceprintEvalResult> results)
    {
        int knownPersonTests = results.Count(r => r.ExpectedPersonId is not null);
        int unknownTests = results.Count - knownPersonTests;
        int personCorrect = results.Count(r => r.PersonCorrect);
        int unknownCandidate = results.Count(r => r.ExpectedPersonId is null && r.HasCandidate);
        double audioSeconds = results.Sum(r => r.DurationMs / 1000.0);
        double wall = results.Sum(r => r.EmbeddingWallMs);
        double cpu = results.Sum(r => r.EmbeddingCpuMs);

        return new VoiceprintEvalSummary(
            results.Count,
            knownPersonTests,
            unknownTests,
            personCorrect,
            unknownCandidate,
            results.Count(r => r.Finite),
            audioSeconds,
            wall,
            cpu,
            results.Sum(r => r.EmbeddingAllocatedBytes),
            knownPersonTests == 0 ? double.NaN : (double)personCorrect / knownPersonTests,
            unknownTests == 0 ? double.NaN : (double)unknownCandidate / unknownTests,
            results.Count == 0 ? double.NaN : results.Average(r => SafeScore(r.BestScore)),
            results.Count == 0 ? double.NaN : results.Average(r => SafeScore(r.Margin)),
            audioSeconds <= 0 ? double.NaN : wall / audioSeconds,
            audioSeconds <= 0 ? double.NaN : cpu / audioSeconds);
    }

    private static double SafeScore(double value) => double.IsFinite(value) ? value : 0.0;
}
