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
        Console.Error.WriteLine("VoiceprintEval calibrate --results results.json [--out thresholds.json]");
        Console.Error.WriteLine("WAV input: 16-bit PCM, 16 kHz, mono or stereo. No diarization, no recheck, no UI.");
    }
}
