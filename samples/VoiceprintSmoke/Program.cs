namespace VoiceprintSmoke;

using System;
using System.Globalization;
using System.IO;
using TranscribeCppSharp;
using TranscribeCppSharp.Voiceprint;

/// <summary>
/// Minimal real-inference smoke test. It does not claim accuracy.
/// Usage: VoiceprintSmoke model.onnx a1.wav a2.wav b1.wav
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: VoiceprintSmoke model.onnx a1.wav a2.wav b1.wav");
            return 2;
        }

        string modelPath = args[0];
        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Model not found: {modelPath}");
            return 2;
        }

        float[] a1 = PcmExtensions.ReadWavToPcm(args[1]);
        float[] a2 = PcmExtensions.ReadWavToPcm(args[2]);
        float[] b1 = PcmExtensions.ReadWavToPcm(args[3]);

        using var embedder = new SherpaOnnxSpeakerEmbedder(
            modelPath,
            modelId: "wespeaker_en_voxceleb_resnet34_LM@sherpa-onnx-1.13.8",
            numThreads: 1);

        var started = DateTimeOffset.UtcNow;
        float[] ea1 = await embedder.EmbedAsync(a1).ConfigureAwait(false);
        float[] ea2 = await embedder.EmbedAsync(a2).ConfigureAwait(false);
        float[] eb1 = await embedder.EmbedAsync(b1).ConfigureAwait(false);
        TimeSpan elapsed = DateTimeOffset.UtcNow - started;

        double simAa = Cosine(ea1, ea2);
        double simAb = Cosine(ea1, eb1);
        double margin = simAa - simAb;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"model={embedder.ModelId}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"dim={embedder.Dimension}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"samples_a1={a1.Length} samples_a2={a2.Length} samples_b1={b1.Length}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"sim_aa={simAa:0.000000}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"sim_ab={simAb:0.000000}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"margin={margin:0.000000}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"three_embeddings_elapsed_ms={elapsed.TotalMilliseconds:0}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"finite={IsFinite(ea1) && IsFinite(ea2) && IsFinite(eb1)}"));

        if (simAa > simAb && IsFinite(ea1) && IsFinite(ea2) && IsFinite(eb1))
        {
            return 0;
        }

        Console.Error.WriteLine("Smoke test failed: sim_aa <= sim_ab or non-finite embedding.");
        return 1;
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;
        int length = Math.Min(a.Length, b.Length);

        for (int i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0.0 || normB <= 0.0)
        {
            return -1.0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
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
}
