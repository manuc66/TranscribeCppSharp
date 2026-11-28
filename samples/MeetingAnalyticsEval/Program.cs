namespace MeetingAnalyticsEval;

using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.MeetingAnalytics.Voiceprint;
using TranscribeCppSharp.Voiceprint;
using Task = System.Threading.Tasks.Task;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || Has(args, "--help"))
        {
            Usage();
            return 0;
        }

        string? corpus = Option(args, "--corpus");
        string? asrModel = Option(args, "--asr-model");
        string? diarizerModel = Option(args, "--diarizer-model");
        string? speakerModel = Option(args, "--speaker-model");
        string? outDir = Option(args, "--out-dir");

        if (corpus is null || asrModel is null || diarizerModel is null || speakerModel is null || outDir is null)
        {
            Usage();
            return 2;
        }

        try
        {
            string modelId = Option(args, "--model-id") ?? "tidyvoicex_samresnet34@hr16-ONNX-sherpa-1.13.8";
            string? modelSha256 = Option(args, "--model-sha256");
            int numThreads = IntOption(args, "--num-threads", 1);
            double minScore = DoubleOption(args, "--min-score", 0.75);
            double minMargin = DoubleOption(args, "--min-margin", 0.10);
            int maxRechecks = IntOption(args, "--max-rechecks", 1);
            int maxSnippets = IntOption(args, "--max-snippets", 3);
            int minTurnMs = IntOption(args, "--min-turn-ms", 0);
            int paddingMs = IntOption(args, "--padding-ms", 0);
            int maxSnippetMs = IntOption(args, "--max-snippet-ms", 0);
            string voiceprintPath = Option(args, "--voiceprints") ?? Path.Combine(outDir, "voiceprints.json");
            string unknownPath = Option(args, "--unknowns") ?? Path.Combine(outDir, "unknown-speakers.json");
            var analyticsOptions = new MeetingAnalyticsOptions(
                RecordingDuration: null,
                MinSilence: TimeSpan.FromSeconds(1),
                MinInterruptedSpeech: TimeSpan.FromMilliseconds(500),
                BackchannelMaxDuration: TimeSpan.FromMilliseconds(1500),
                MinOverlap: TimeSpan.Zero);
            var resolutionOptions = new MeetingSpeakerResolutionOptions(
                IdentityPolicy: new AmbiguityPolicy(minScore, minMargin, maxRechecks),
                AnalyticsOptions: analyticsOptions,
                PersistUnknownSpeakers: true,
                MaxSnippetsPerSpeaker: maxSnippets,
                MinTurnMs: minTurnMs,
                PaddingMs: paddingMs,
                MaxSnippetMs: maxSnippetMs,
                ModelSha256: modelSha256);
            var runner = new MeetingEvalRunner(
                asrModel,
                diarizerModel,
                speakerModel,
                modelId,
                modelSha256,
                numThreads,
                voiceprintPath,
                unknownPath,
                resolutionOptions.IdentityPolicy,
                resolutionOptions);

            await runner.RunAsync(corpus, outDir).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MeetingAnalyticsEval failed: {ex.Message}");
            return 1;
        }
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
        Console.Error.WriteLine("MeetingAnalyticsEval --corpus corpus.json --asr-model asr.gguf --diarizer-model diar.gguf --speaker-model speaker.onnx --out-dir out");
        Console.Error.WriteLine("Options: --voiceprints file.json --unknowns file.json --model-id ID --model-sha256 SHA --num-threads N");
        Console.Error.WriteLine("         --min-score X --min-margin X --max-rechecks N --max-snippets N --min-turn-ms N --padding-ms N --max-snippet-ms N");
        Console.Error.WriteLine("Corpus expectedSpeakers keys are per-run numeric ids; null means unknown. No nl, no production threshold.");
    }
}
