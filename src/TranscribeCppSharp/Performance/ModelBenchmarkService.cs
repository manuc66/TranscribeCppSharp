using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;

namespace TranscribeCppSharp.Performance;

/// <summary>
/// Times a transcription on this machine, so the model list can be sorted by
/// what is actually fast here rather than by what is supposed to be fast.
/// </summary>
/// <remarks>
/// Only for models already on disk. Nothing here downloads anything: a model
/// that has to be fetched first cannot be compared against one that is loaded,
/// and the 72 aliases come to 58 GiB.
/// <para>
/// The measurement is deliberately not a single pass. One run reports whatever
/// else the machine was doing at that instant, and docs/streaming-bench.md
/// records a case where contention turned 0.218 into 33 — a factor of 150. So
/// there is one discarded warm-up (the first pass pays for lazy allocation
/// inside the model) and <see cref="Passes"/> timed ones, and the best is kept:
/// contention only ever makes a run slower, so the fastest pass is the one
/// closest to what the machine can do.
/// </para>
/// </remarks>
public static class ModelBenchmarkService
{
    /// <summary>Default length of the excerpt taken from the user's audio.</summary>
    public const int DefaultExcerptSeconds = 30;

    /// <summary>Timed passes kept after the warm-up.</summary>
    public const int Passes = 3;

    /// <summary>
    /// Times one model over an excerpt of <paramref name="audioPath"/>.
    /// </summary>
    /// <param name="descriptor">The model to measure; must already be cached.</param>
    /// <param name="audioPath">Audio file to take the excerpt from.</param>
    /// <param name="excerptSeconds">Length of the excerpt to time.</param>
    /// <param name="backend">Compute backend to request.</param>
    /// <param name="device">Device to request, or null to let it be resolved.</param>
    /// <param name="threads">Session thread count, or null for the default.</param>
    /// <param name="progress">Reports 0..1 across the passes.</param>
    /// <param name="cancellationToken">Cancels between passes.</param>
    /// <returns>The measurement and the conditions it was taken under.</returns>
    /// <exception cref="InvalidOperationException">
    /// The model is not on disk, or the audio holds no samples.
    /// </exception>
    public static ModelBenchmark Measure(
        ModelDescriptor descriptor,
        string audioPath,
        int excerptSeconds,
        BackendRequest? backend,
        BackendDevice? device,
        int? threads,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string modelPath = ModelStore.CachedPath(descriptor);
        if (!File.Exists(modelPath))
        {
            throw new InvalidOperationException($"'{descriptor.Alias}' is not on disk; download it first.");
        }

        float[] pcm = ReadExcerpt(audioPath, excerptSeconds, out double audioSeconds);
        if (pcm.Length == 0)
        {
            throw new InvalidOperationException($"No audio could be read from '{audioPath}'.");
        }

        var loadWatch = Stopwatch.StartNew();
        using Model model = Model.Load(modelPath, builder =>
        {
            if (backend != null)
            {
                builder.WithBackend(backend.Value);
            }

            if (device != null)
            {
                builder.WithDevice(device);
            }
        });
        loadWatch.Stop();

        // The device the model actually resolved to, not the one requested:
        // AUTO falls back, and a row claiming a GPU run that happened on the CPU
        // would be wrong.
        BackendDevice? resolved = model.Device;

        using Session session = model.CreateSession(sessionParams =>
        {
            if (threads.HasValue)
            {
                sessionParams.WithThreads(threads.Value);
            }
        });

        // Warm-up, discarded: the first Run pays for whatever the model allocates
        // lazily, which is not a cost the second run pays again.
        session.Run(pcm, null, cancellationToken);

        double bestRtf = double.MaxValue;
        double bestEncode = 0;
        double bestDecode = 0;

        for (int pass = 0; pass < Passes; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var watch = Stopwatch.StartNew();
            Transcript transcript = session.Run(pcm, null, cancellationToken);
            watch.Stop();

            double rtf = watch.Elapsed.TotalSeconds / audioSeconds;
            if (rtf < bestRtf)
            {
                bestRtf = rtf;
                bestEncode = transcript.Timing?.EncodeMs ?? 0;
                bestDecode = transcript.Timing?.DecodeMs ?? 0;
            }

            progress?.Report((double)(pass + 1) / Passes);
        }

        return new ModelBenchmark
        {
            Alias = descriptor.Alias,
            Rtf = bestRtf,
            LoadMs = loadWatch.Elapsed.TotalMilliseconds,
            EncodeMs = bestEncode > 0 ? bestEncode : null,
            DecodeMs = bestDecode > 0 ? bestDecode : null,
            Passes = Passes,
            AudioSeconds = audioSeconds,
            AudioName = Path.GetFileName(audioPath),
            Backend = (backend ?? BackendRequest.BackendAuto).ToString(),
            Device = Describe(resolved),
            Threads = threads ?? 0,
            MachineKey = MachineKey(),
            MeasuredAt = DateTimeOffset.Now,
        };
    }

    /// <summary>
    /// Reads at most <paramref name="seconds"/> of audio from the start of a file.
    /// </summary>
    /// <param name="audioPath">The file to read.</param>
    /// <param name="seconds">How much to take.</param>
    /// <param name="actualSeconds">Length actually read, in seconds.</param>
    /// <returns>The samples, at 16 kHz mono.</returns>
    /// <remarks>
    /// The start of the file, not a slice chosen for being speech. Picking a
    /// good excerpt by ear would make the number depend on the pick, and
    /// silence at the head is exactly what a real recording often has.
    /// </remarks>
    private static float[] ReadExcerpt(string audioPath, int seconds, out double actualSeconds)
    {
        int wanted = seconds * WindowPlanner.SampleRate;
        using PcmSource source = AudioLoader.Open(audioPath);
        int length = (int)Math.Min(Math.Min(source.LengthSamples, wanted), int.MaxValue);
        actualSeconds = length / (double)WindowPlanner.SampleRate;
        return source.ReadWindow(0, length);
    }

    private static string Describe(BackendDevice? device)
        => device is null ? "default" : string.IsNullOrWhiteSpace(device.Name) ? device.Kind : device.Name;

    /// <summary>
    /// Builds the fingerprint a result is only valid under.
    /// </summary>
    /// <returns>A short string identifying the OS, core count and backend version.</returns>
    /// <remarks>
    /// Deliberately does not name the device. The device a run lands on depends on
    /// the model and on the backend requested — a 16 GiB model may fall back to the
    /// CPU where a small one stays on the GPU — so a per-run device in the key
    /// would give two models measured on the same machine different keys and hide
    /// both from the comparison. The device actually used is recorded on the result
    /// and shown in its conditions line.
    /// <para>
    /// What this misses: it does not notice a part swapped for an identical one, and
    /// two machines that agree on these four values are indistinguishable.
    /// </para>
    /// </remarks>
    public static string MachineKey()
    {
        string os = RuntimeInformation.OSDescription;
        int cores = Environment.ProcessorCount;
        return $"{os}|{cores}|{TryBackendVersion()}";
    }

    private static string TryBackendVersion()
    {
        try
        {
            return $"{Backends.Version}+{Backends.VersionCommit}";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TranscribeException)
        {
            return "unknown";
        }
    }
}
