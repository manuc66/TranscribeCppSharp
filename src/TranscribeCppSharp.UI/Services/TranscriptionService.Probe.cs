using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;

namespace TranscribeCppSharp.UI.Services;

public partial class TranscriptionService
{
    /// <summary>
    /// Runs at most one window of the audio with the selected settings and reports
    /// whether it held, without producing a transcript.
    /// </summary>
    /// <remarks>
    /// The allocation that fails in a real run happens when the native call builds
    /// the graph for its input; running the same window is what exercises it. It
    /// does not test later windows, the diarizer, or the decoder's output length —
    /// the button's tooltip and the page say so.
    /// </remarks>
    public async Task<MemoryProbeResult> ProbeMemoryAsync(
        string audioPath,
        string modelPath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            Model model = LoadModel(modelPath, options.BackendRequest, options.Device);

            try
            {
                using var audioSource = AudioLoader.Open(audioPath);
                using var session = CreateSession(model, options);

                long windowSamples = Math.Max(1, options.WindowSeconds) * (long)WindowPlanner.SampleRate;
                int count = (int)Math.Min(audioSource.LengthSamples, Math.Min(windowSamples, int.MaxValue));
                if (count <= 0)
                {
                    return new MemoryProbeResult(false, false, null, "The audio is empty.");
                }

                var samples = audioSource.ReadWindow(0, count);
                var memory = new MemorySampler(model);
                memory.Sample();

                try
                {
                    session.Run(samples, builder => Configure(builder, options), cancellationToken);
                    memory.Sample();
                    return new MemoryProbeResult(true, false, memory.Observation, null);
                }
                catch (TranscribeException ex) when (ex.StatusCode == Status.ErrOom)
                {
                    memory.Sample();
                    return new MemoryProbeResult(false, true, memory.Observation, ex.Message);
                }
            }
            finally
            {
                model.Dispose();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves <paramref name="modelArgument"/> to a local file and loads it.
    /// </summary>
    /// <remarks>
    /// The argument may be a curated alias, a HuggingFace spec or a plain path,
    /// exactly as for the CLI: the view models bind an alias from the model
    /// list, and Model.Load only accepts a real file. Resolution blocks while it
    /// downloads a first-use model, so it runs on a worker thread.
    /// </remarks>
    private static Model LoadModel(
        string modelArgument,
        BackendRequest? backend,
        BackendDevice? device)
    {
        string resolved = ModelStore.Resolve(modelArgument, null, TextWriter.Null);
        EnforceWeightsFit(resolved, backend, device);
        return Model.Load(resolved, builder =>
        {
            if (backend != null)
                builder.WithBackend(backend.Value);
            if (device != null)
                builder.WithDevice(device);
        });
    }

    /// <summary>
    /// Refuses a model whose weight file alone cannot fit in the free memory of
    /// the device the request resolves to.
    /// </summary>
    /// <remarks>
    /// Only the certain case is refused: the compute graph's size is not exposed by
    /// the native API, so a run that fits its weights can still run out of memory
    /// later. When the device or its free figure is unknown, this says nothing and
    /// lets the load report the real failure.
    /// </remarks>
    private static void EnforceWeightsFit(string modelPath, BackendRequest? backend, BackendDevice? device)
    {
        long modelBytes;
        try
        {
            modelBytes = new FileInfo(modelPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        IReadOnlyList<BackendDevice> devices;
        try
        {
            devices = Backends.EnumerateDevices();
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Without a live device list there is nothing factual to compare.
            return;
        }

        // Prefer the live entry for a pinned device: its free figure is a snapshot.
        BackendDevice? pinned = device is null
            ? null
            : devices.FirstOrDefault(d => d.Handle == device.Handle && d.Handle != IntPtr.Zero)
                ?? devices.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceId) && d.DeviceId == device.DeviceId)
                ?? device;

        BackendDevice? target = MemoryPreflight.Target(devices, backend, pinned);
        if (!MemoryPreflight.WeightsFit(modelBytes, target))
        {
            throw new InvalidOperationException(
                $"The model file is {MemoryPreflight.FormatBytes((ulong)modelBytes)}; "
                + $"{target!.Name} reports {MemoryPreflight.FormatBytes(target.MemoryFree)} free of "
                + $"{MemoryPreflight.FormatBytes(target.MemoryTotal)}. The weights alone do not fit on that device. "
                + "Pin a device with more free memory in Settings, or choose a smaller quantization.");
        }
    }

    /// <summary>
    /// Creates a session and applies the session-level tuning.
    /// </summary>
    /// <remarks>
    /// Backend and device are not set here: they belong to the model, and
    /// <see cref="LoadModel"/> applies them. <see cref="StreamOptions"/> has no
    /// session tuning at all, so streaming goes through the plain
    /// <c>CreateSession</c> overload.
    /// </remarks>
    private static Session CreateSession(
        Model model,
        TranscriptionOptions options)
        => model.CreateSession(sessionParams =>
        {
            if (options.Threads.HasValue)
                sessionParams.WithThreads(options.Threads.Value);
            if (options.KvType.HasValue)
                sessionParams.WithKvType(options.KvType.Value);
            if (options.ContextSize.HasValue)
                sessionParams.WithContextSize(options.ContextSize.Value);
        });

    /// <summary>
    /// Applies every set field of <paramref name="options"/> to one run.
    /// </summary>
    /// <remarks>
    /// The Whisper extension is attached only when at least one of its fields is
    /// set. The condition covers all eleven, not just the prompt and the
    /// temperature: a run configured with, say, only a seed used to build no
    /// extension at all and silently dropped it.
    /// </remarks>
    private static void Configure(RunParamsBuilder builder, TranscriptionOptions options)
    {
        builder
            .WithTimestamps(options.TimestampKind)
            .WithDiarize(options.DiarizeMode);

        if (options.Language != "auto")
            builder.WithLanguage(options.Language);

        if (options.PncMode.HasValue)
            builder.WithPnc(options.PncMode.Value);
        if (options.ItnMode.HasValue)
            builder.WithItn(options.ItnMode.Value);
        if (options.Task.HasValue)
            builder.WithTask(options.Task.Value);
        if (options.TargetLanguage != null)
            builder.WithTargetLanguage(options.TargetLanguage);
        if (options.SpecKDrafts.HasValue)
            builder.WithSpecKDrafts(options.SpecKDrafts.Value);
        if (options.KeepSpecialTags.HasValue)
            builder.WithKeepSpecialTags(options.KeepSpecialTags.Value);
        if (options.Vocabulary is { Length: > 0 })
            builder.WithVocabulary(options.Vocabulary);

        if (!HasWhisperExtension(options))
            return;

        var whisperExt = new WhisperExtBuilder();
        if (options.WhisperInitialPrompt != null)
            whisperExt.WithInitialPrompt(options.WhisperInitialPrompt);
        if (options.WhisperTemperature.HasValue)
            whisperExt.WithTemperature(options.WhisperTemperature.Value);
        if (options.WhisperTemperatureInc.HasValue)
            whisperExt.WithTemperatureInc(options.WhisperTemperatureInc.Value);
        if (options.WhisperCompressionRatioThold.HasValue)
            whisperExt.WithCompressionRatioThold(options.WhisperCompressionRatioThold.Value);
        if (options.WhisperLogprobThold.HasValue)
            whisperExt.WithLogprobThold(options.WhisperLogprobThold.Value);
        if (options.WhisperNoSpeechThold.HasValue)
            whisperExt.WithNoSpeechThold(options.WhisperNoSpeechThold.Value);
        if (options.WhisperSeed.HasValue)
            whisperExt.WithSeed(options.WhisperSeed.Value);
        if (options.WhisperMaxInitialTimestamp.HasValue)
            whisperExt.WithMaxInitialTimestamp(options.WhisperMaxInitialTimestamp.Value);
        if (options.WhisperConditionOnPrevTokens)
            whisperExt.WithConditionOnPrevTokens(options.WhisperConditionOnPrevTokens);
        if (options.WhisperMaxPrevContextTokens.HasValue)
            whisperExt.WithMaxPrevContextTokens(options.WhisperMaxPrevContextTokens.Value);

        builder.WithWhisperExt(whisperExt);
    }

    private static bool HasWhisperExtension(TranscriptionOptions options)
        => options.WhisperInitialPrompt != null
        || options.WhisperTemperature.HasValue
        || options.WhisperTemperatureInc.HasValue
        || options.WhisperCompressionRatioThold.HasValue
        || options.WhisperLogprobThold.HasValue
        || options.WhisperNoSpeechThold.HasValue
        || options.WhisperSeed.HasValue
        || options.WhisperMaxInitialTimestamp.HasValue
        || options.WhisperConditionOnPrevTokens
        || options.WhisperMaxPrevContextTokens.HasValue;

    private static void Configure(StreamParamsBuilder builder, StreamOptions options)
    {
        builder.WithCommitPolicy(options.CommitPolicy);

        if (options.StablePrefixAgreement.HasValue)
            builder.WithStablePrefixAgreement(options.StablePrefixAgreement.Value);

        if (options.MoonshineMinDecodeIntervalMs.HasValue)
        {
            var moonshineExt = new MoonshineExtBuilder();
            moonshineExt.WithMinDecodeIntervalMs(options.MoonshineMinDecodeIntervalMs.Value);
            builder.WithMoonshineExt(moonshineExt);
        }

        if (options.ParakeetAttContextRight.HasValue)
        {
            var parakeetExt = new ParakeetStreamExtBuilder();
            parakeetExt.WithAttContextRight(options.ParakeetAttContextRight.Value);
            builder.WithParakeetStreamExt(parakeetExt);
        }

        if (options.ParakeetLeftMs.HasValue || options.ParakeetChunkMs.HasValue || options.ParakeetRightMs.HasValue)
        {
            var parakeetBuffered = new ParakeetBufferedStreamExtBuilder();
            if (options.ParakeetLeftMs.HasValue)
                parakeetBuffered.WithLeftMs(options.ParakeetLeftMs.Value);
            if (options.ParakeetChunkMs.HasValue)
                parakeetBuffered.WithChunkMs(options.ParakeetChunkMs.Value);
            if (options.ParakeetRightMs.HasValue)
                parakeetBuffered.WithRightMs(options.ParakeetRightMs.Value);
            builder.WithParakeetBufferedStreamExt(parakeetBuffered);
        }

        if (options.VoxtralNumDelayTokens.HasValue || options.VoxtralMinDecodeIntervalMs.HasValue)
        {
            var voxtralExt = new VoxtralExtBuilder();
            if (options.VoxtralNumDelayTokens.HasValue)
                voxtralExt.WithNumDelayTokens(options.VoxtralNumDelayTokens.Value);
            if (options.VoxtralMinDecodeIntervalMs.HasValue)
                voxtralExt.WithMinDecodeIntervalMs(options.VoxtralMinDecodeIntervalMs.Value);
            builder.WithVoxtralExt(voxtralExt);
        }
    }

    /// <summary>
    /// Folds one window's transcript into the run.
    /// </summary>
    /// <remarks>
    /// The merger keeps segments and drops the ones the previous window already
    /// covered. Words and speaker rows are appended unfiltered and only shifted:
    /// the native layer emits them per window with no seam marker, so there is
    /// nothing here that can tell a repeated word from a new one, and a caller
    /// wanting gapless words has to reconcile them itself.
    /// </remarks>
    private static void Collect(
        TranscriptMerger merger,
        AudioWindow window,
        Transcript transcript,
        List<WordResult> words,
        List<SpeakerSegmentResult> speakerSegments)
    {
        merger.Add(window, transcript);

        foreach (var word in transcript.Words)
        {
            words.Add(TranscriptMerger.Shift(window, word));
        }

        foreach (var speaker in transcript.SpeakerSegments)
        {
            speakerSegments.Add(TranscriptMerger.Shift(window, speaker));
        }
    }

    /// <summary>
    /// Records the lowest free figure the model's device reports while a run is in
    /// progress. <see cref="Model.Device"/> queries the native device live, so a
    /// sample costs one interop call per window.
    /// </summary>
    /// <remarks>
    /// This is a lower bound, not a peak: the figure is read between windows, while
    /// the per-window compute buffer is not visible from here. It is still the
    /// number the next run is compared against, and it is a measurement rather than
    /// an estimate. A device that reports no free figure yields no observation.
    /// </remarks>
    private sealed class MemorySampler(Model model)
    {
        private string _deviceName = string.Empty;
        private ulong _total;
        private ulong _minimumFree = ulong.MaxValue;
        private bool _observed;

        public MemoryObservation? Observation => _observed
            ? new MemoryObservation(_deviceName, _total, _minimumFree, DateTimeOffset.Now)
            : null;

        /// <summary>
        /// Starts a new measurement. Used before a retry at a smaller window: the
        /// failed attempt's low point belongs to its error report, not to the run
        /// that eventually succeeds.
        /// </summary>
        public void Reset()
        {
            _deviceName = string.Empty;
            _total = 0;
            _minimumFree = ulong.MaxValue;
            _observed = false;
        }

        public void Sample()
        {
            try
            {
                BackendDevice? device = model.Device;
                if (device is null || device.MemoryFree == 0)
                {
                    return;
                }

                if (!_observed)
                {
                    _deviceName = device.Name;
                    _total = device.MemoryTotal;
                    _minimumFree = device.MemoryFree;
                    _observed = true;
                    return;
                }

                _total = Math.Max(_total, device.MemoryTotal);
                _minimumFree = Math.Min(_minimumFree, device.MemoryFree);
            }
            catch (Exception ex) when (ex is TranscribeException or ObjectDisposedException
                                          or DllNotFoundException or EntryPointNotFoundException)
            {
                // A measurement must never fail a run; without it the UI simply
                // has nothing to compare next time.
            }
        }
    }
}
