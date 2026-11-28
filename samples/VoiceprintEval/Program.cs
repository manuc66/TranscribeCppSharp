namespace VoiceprintEval;

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return 2;
        }

        if (string.Equals(args[0], "eval", StringComparison.OrdinalIgnoreCase))
        {
            string? model = GetOption(args, "--model");
            string? manifest = GetOption(args, "--manifest");
            string? outPath = GetOption(args, "--out");
            int numThreads = int.TryParse(GetOption(args, "--num-threads"), out int parsedThreads) ? parsedThreads : 1;

            if (model is null || manifest is null)
            {
                Usage();
                return 2;
            }

            string modelId = GetOption(args, "--model-id")
                ?? Path.GetFileNameWithoutExtension(model) + "@sherpa-onnx-1.13.8";
            var runner = new VoiceprintEvalRunner(model, modelId: modelId, numThreads: numThreads);
            VoiceprintEvalResults results = await runner.RunAsync(manifest).ConfigureAwait(false);
            string json = JsonSerializer.Serialize(results, JsonOptions);

            if (outPath is null)
            {
                Console.WriteLine(json);
            }
            else
            {
                await File.WriteAllTextAsync(outPath, json).ConfigureAwait(false);
                Console.WriteLine($"wrote {outPath}");
                Console.WriteLine(JsonSerializer.Serialize(results.Summary, JsonOptions));
            }

            return 0;
        }

        if (string.Equals(args[0], "calibrate", StringComparison.OrdinalIgnoreCase))
        {
            string? resultsPath = GetOption(args, "--results");
            string? outPath = GetOption(args, "--out");

            if (resultsPath is null)
            {
                Usage();
                return 2;
            }

            string json = await File.ReadAllTextAsync(resultsPath).ConfigureAwait(false);
            VoiceprintEvalResults results = JsonSerializer.Deserialize<VoiceprintEvalResults>(json, JsonOptions)
                ?? throw new InvalidDataException("Empty results.");
            bool byLanguage = args.Any(a => string.Equals(a, "--by-language", StringComparison.Ordinal));
            string output = byLanguage
                ? JsonSerializer.Serialize(ThresholdCalibrator.CalibrateByLanguage(results.Results), JsonOptions)
                : JsonSerializer.Serialize(ThresholdCalibrator.Calibrate(results), JsonOptions);

            if (outPath is null)
            {
                Console.WriteLine(output);
            }
            else
            {
                await File.WriteAllTextAsync(outPath, output).ConfigureAwait(false);
                Console.WriteLine($"wrote {outPath}");
            }

            return 0;
        }


        if (string.Equals(args[0], "enroll", StringComparison.OrdinalIgnoreCase))
        {
            string? model = GetOption(args, "--model");
            string? manifest = GetOption(args, "--manifest");
            string? storePath = GetOption(args, "--store");
            int numThreads = int.TryParse(GetOption(args, "--num-threads"), out int parsedThreads) ? parsedThreads : 1;

            if (model is null || manifest is null || storePath is null)
            {
                Usage();
                return 2;
            }

            string modelId = GetOption(args, "--model-id")
                ?? Path.GetFileNameWithoutExtension(model) + "@sherpa-onnx-1.13.8";
            string? modelSha256 = GetOption(args, "--model-sha256");
            string schemaVersion = GetOption(args, "--schema-version") ?? "1";
            string json = await File.ReadAllTextAsync(manifest).ConfigureAwait(false);
            VoiceprintEvalCorpus corpus = JsonSerializer.Deserialize<VoiceprintEvalCorpus>(json, JsonOptions) ?? new VoiceprintEvalCorpus();
            string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifest)) ?? Directory.GetCurrentDirectory();
            var store = new TranscribeCppSharp.Voiceprint.JsonVoiceprintStore(storePath);
            using var embedder = new TranscribeCppSharp.Voiceprint.SherpaOnnxSpeakerEmbedder(model, modelId, numThreads);

            foreach (VoiceprintEvalPerson person in corpus.Persons)
            {
                var embeddings = new List<float[]>();
                foreach (string enrollment in person.Enrollment)
                {
                    string path = Path.GetFullPath(Path.Combine(baseDirectory, enrollment));
                    float[] pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path);
                    embeddings.Add(await embedder.EmbedAsync(pcm).ConfigureAwait(false));
                }

                var voiceprint = new TranscribeCppSharp.Voiceprint.Voiceprint(
                    person.Id,
                    string.IsNullOrWhiteSpace(person.DisplayName) ? person.Id : person.DisplayName,
                    modelId,
                    embedder.Dimension,
                    embeddings,
                    DateTimeOffset.UtcNow,
                    embeddings.Count,
                    schemaVersion,
                    modelSha256);

                await store.AddOrUpdateAsync(voiceprint).ConfigureAwait(false);
            }

            Console.WriteLine($"wrote {storePath}");
            return 0;
        }

        if (string.Equals(args[0], "identify", StringComparison.OrdinalIgnoreCase))
        {
            string? model = GetOption(args, "--model");
            string? audio = GetOption(args, "--audio");
            string? storePath = GetOption(args, "--store");
            int numThreads = int.TryParse(GetOption(args, "--num-threads"), out int parsedThreads) ? parsedThreads : 1;

            if (model is null || audio is null || storePath is null)
            {
                Usage();
                return 2;
            }

            string modelId = GetOption(args, "--model-id")
                ?? Path.GetFileNameWithoutExtension(model) + "@sherpa-onnx-1.13.8";
            double minScore = GetOption(args, "--min-score") is { } minScoreText && double.TryParse(minScoreText, out double parsedMinScore) ? parsedMinScore : 0.75;
            double minMargin = GetOption(args, "--min-margin") is { } minMarginText && double.TryParse(minMarginText, out double parsedMinMargin) ? parsedMinMargin : 0.0;
            int maxRechecks = int.TryParse(GetOption(args, "--max-rechecks"), out int parsedMaxRechecks) ? parsedMaxRechecks : 0;
            var policy = new TranscribeCppSharp.Voiceprint.AmbiguityPolicy(minScore, minMargin, maxRechecks);
            var recheckEmbeddings = new List<float[]>();

            using var embedder = new TranscribeCppSharp.Voiceprint.SherpaOnnxSpeakerEmbedder(model, modelId, numThreads);
            float[] pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(audio);
            float[] embedding = await embedder.EmbedAsync(pcm).ConfigureAwait(false);

            if (GetOption(args, "--recheck-audio") is { } recheckAudio)
            {
                foreach (string recheckPath in recheckAudio.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    float[] recheckPcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(recheckPath);
                    recheckEmbeddings.Add(await embedder.EmbedAsync(recheckPcm).ConfigureAwait(false));
                }
            }

            var store = new TranscribeCppSharp.Voiceprint.JsonVoiceprintStore(storePath);
            var voiceprints = await store.LoadAsync().ConfigureAwait(false);
            var match = TranscribeCppSharp.Voiceprint.VoiceprintMatcher.MatchWithRechecks(
                embedding,
                modelId,
                voiceprints,
                policy,
                recheckEmbeddings);
            string output = JsonSerializer.Serialize(match, JsonOptions);

            if (GetOption(args, "--out") is { } outPath)
            {
                await File.WriteAllTextAsync(outPath, output).ConfigureAwait(false);
                Console.WriteLine($"wrote {outPath}");
            }
            else
            {
                Console.WriteLine(output);
            }

            return 0;
        }

        Usage();
        return 2;
    }

    private static string? GetOption(string[] args, string name)
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

    private static void Usage()
    {
        Console.Error.WriteLine("VoiceprintEval eval --model model.onnx --manifest corpus.json [--out results.json] [--num-threads N] [--model-id ID]");
        Console.Error.WriteLine("VoiceprintEval enroll --model model.onnx --manifest corpus.json --store voiceprints.json\nVoiceprintEval identify --model model.onnx --store voiceprints.json --audio test.wav\nVoiceprintEval calibrate --results results.json [--out thresholds.json]");
        Console.Error.WriteLine("WAV input: 16-bit PCM, 16 kHz, mono or stereo. No diarization, no recheck, no UI.");
    }
}
