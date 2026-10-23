using System.Diagnostics;
using System.Globalization;
using System.Text;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;

namespace TranscribeCppSharp.Cli;

internal static partial class TranscribeCommand
{
    private static int RunWithAudio(
        TextWriter stdout,
        TextWriter stderr,
        CliOptions options,
        string modelPath,
        DeviceChoice choice,
        PcmSource pcmSource)
    {
        long totalSamples = pcmSource.LengthSamples;
        if (totalSamples > int.MaxValue)
        {
            stderr.WriteLine(
                $"the audio is {totalSamples} samples, more than this tool can index. Split it into shorter files.");
            return 1;
        }

        stdout.WriteLine($"audio : {Fmt.Samples(totalSamples)} samples = {totalSamples / (double)WindowPlanner.SampleRate:F1}s @ 16kHz mono");

        using var model = Model.Load(modelPath, p =>
        {
            p.WithBackend(choice.Backend);
            if (choice.Device is not null)
            {
                p.WithDevice(choice.Device);
            }
        });
        string modelLabel = $"{model.Architecture}/{model.Variant}";
        stdout.WriteLine($"model : {modelLabel}");
        stdout.WriteLine($"compute: {DescribeCompute(choice, model)}");

        // transcribe.cpp 0.4 gives a model one or more roles. An ASR model keeps
        // the transcription path; anything else is a diarize/langid/vad model and
        // runs through its own session, which is what the role reports print.
        TranscribeRole roles = model.Roles;
        stdout.WriteLine($"roles : {DescribeRoles(roles)}");

        if (!roles.HasFlag(TranscribeRole.Asr))
        {
            return RunNonAsrRole(stdout, stderr, options, model, modelLabel, pcmSource, totalSamples, roles);
        }

        stdout.WriteLine($"diarization supported: {model.Supports(Feature.FeatureDiarization)}");

        using var session = model.CreateSession();
        var limits = session.GetLimits();
        int maxWindowMs = limits.EffectiveMaxAudioMs > 0
            ? (int)Math.Clamp(limits.EffectiveMaxAudioMs - WindowPlanner.OverlapMs, 1, int.MaxValue)
            : int.MaxValue;
        int windowMs = (int)Math.Min((long)options.ChunkSeconds * 1000, maxWindowMs);
        stdout.WriteLine($"window: {windowMs / 1000}s (model max audio {limits.EffectiveMaxAudioMs / 1000}s)");

        IReadOnlyList<AudioWindow> windows;
        if (options.VadSpec is null)
        {
            if (!WindowPlanner.TryPlan((int)totalSamples, windowMs, out windows, out var planError))
            {
                // The planner's message names no flag, because the GUI shares it.
                // --chunk is rejected earlier in CliOptions with its own message, so
                // reaching here means the audio is empty rather than the window too
                // short, and no flag advice applies.
                stderr.WriteLine(planError);
                return 1;
            }
        }
        else
        {
            int planned = PlanFromSpeech(stdout, stderr, options, choice, pcmSource, totalSamples, windowMs, out windows);
            if (planned != 0)
            {
                return planned;
            }
        }

        var merged = new StringBuilder();

        // JSON, HTML and the two subtitle formats need the segments kept: those
        // documents are written once, at the end, from the complete list. The
        // plain and VTT writers emit each segment as it arrives and their footer
        // ignores the list entirely, so accumulating it there retained a list
        // entry and a string per segment that nothing ever read — one hour of
        // speech is ~900 segments.
        var lines = options.Format is TranscriptFormat.Json or TranscriptFormat.Html
            or TranscriptFormat.Srt or TranscriptFormat.Ass
            ? new List<TranscriptLine>()
            : null;

        // The merger owns the seam bookkeeping (drop what the previous window
        // already reported, shift timestamps into file time) so the CLI and the
        // GUI cannot drift apart. It is told not to retain: segments are written
        // out as each window arrives, and keeping a second copy of ~900
        // segments an hour of speech produces is what "retain: false" avoids.
        var merger = new TranscriptMerger(retain: false);
        var overall = Stopwatch.StartNew();

        StreamWriter? outFile;
        try
        {
            outFile = OpenOutput(options.OutPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // The parent directory is checked while parsing, so reaching this is
            // a permission or a path problem: report it, do not dump a stack trace.
            stderr.WriteLine($"--out: cannot write to {options.OutPath}: {ex.Message}");
            return 1;
        }

        using (outFile)
        {
            // One timestamp for the whole run: the header and the footer of a
            // file must not disagree by the few seconds a long transcription
            // takes, so both read generated from here.
            var info = new TranscriptInfo(
                options.AudioPath,
                TimeSpan.FromSeconds(totalSamples / (double)WindowPlanner.SampleRate),
                modelLabel, options.Language, options.Diarize, windowMs / 1000, DateTime.Now);

            if (outFile is not null)
            {
                TranscriptWriter.WriteHeader(outFile, options.Format, info);
            }

            double audioTotalSec = totalSamples / (double)WindowPlanner.SampleRate;
            for (int chunkIndex = 0; chunkIndex < windows.Count; chunkIndex++)
            {
                AudioWindow window = windows[chunkIndex];
                int offset = window.OffsetSamples;
                int len = window.LengthSamples;

                // This window and nothing else is in memory. The previous code
                // sliced the whole-file array here, which meant an hour of audio
                // (219 MiB) was resident from the load until the last window
                // finished; now the peak is one window, 19 MiB at 300 s.
                var chunk = pcmSource.ReadWindow(offset, len);
                long chunkStartMs = window.StartMs;

                stdout.WriteLine($"\n=== window {chunkIndex + 1} @ {Fmt.Ts(chunkStartMs)} ({Fmt.Samples(len)} samples) ===");
                var windowSw = Stopwatch.StartNew();

                Transcript transcript;
                try
                {
                    transcript = session.Run(chunk, r =>
                    {
                        r.WithDiarize(options.Diarize);
                        r.WithLanguage(options.Language);
                    });
                }
                catch (TranscribeException ex)
                {
                    stderr.WriteLine($"  transcription failed: {ex.Message}");
                    return 1;
                }

                stdout.WriteLine($"  lang   : {transcript.DetectedLanguage}   aborted: {transcript.WasAborted}  truncated: {transcript.WasTruncated}");
                stdout.WriteLine($"  full   : {transcript.FullText.Trim()}");
                if (options.Diarize != DiarizeMode.DiarizeModeOff || transcript.RawText.Length > 0)
                {
                    stdout.WriteLine($"  raw    : {transcript.RawText.Trim()}");
                }

                double windowSec = windowSw.Elapsed.TotalSeconds;
                double audioSec = len / (double)WindowPlanner.SampleRate;
                double processedSec = (offset + len) / (double)WindowPlanner.SampleRate;
                double remainingSec = audioTotalSec - processedSec;
                double rtf = windowSec / audioSec;
                stdout.WriteLine($"  time   : {Fmt.Dur(windowSec)}/ {audioSec:0.0}s audio (RTF {rtf:0.0}x) | done {Fmt.Dur(processedSec)} / {Fmt.Dur(audioTotalSec)} | ETA ~{Fmt.Dur(remainingSec * rtf)}");

                foreach (var seg in merger.Add(window, transcript))
                {
                    var text = Fmt.Normalize(seg.Text);
                    var start = (long)seg.Start.TotalMilliseconds;
                    var end = (long)seg.End.TotalMilliseconds;
                    var line = new TranscriptLine(start, end, seg.SpeakerId, text);
                    lines?.Add(line);

                    if (outFile is not null)
                    {
                        TranscriptWriter.WriteSegment(outFile, options.Format, line);
                        stdout.WriteLine($"  -> [{Fmt.Ts(start)} -> {Fmt.Ts(end)}] Speaker {seg.SpeakerId}: {text}");
                    }

                    merged.AppendLine(CultureInfo.CurrentCulture, $"[{Fmt.Ts(start)} -> {Fmt.Ts(end)}] Speaker {seg.SpeakerId}: {text}");
                }
            }

            if (outFile is not null)
            {
                // The plain and VTT footers ignore the segment list, so passing
                // an empty one is what the non-JSON/HTML path now has.
                TranscriptWriter.WriteFooter(outFile, options.Format, info, lines ?? []);
                stdout.WriteLine($"\ntranscript written to: {options.OutPath} ({FormatName(options.Format)})");
            }

            double totalSec = overall.Elapsed.TotalSeconds;
            stdout.WriteLine("\n=== summary ===");
            stdout.WriteLine($"  total  : {Fmt.Dur(totalSec)} (RTF {totalSec / audioTotalSec:0.0}x) for {Fmt.Dur(audioTotalSec)} audio");
            stdout.WriteLine($"  windows: {windows.Count}");
            if (options.OutPath is not null)
            {
                stdout.WriteLine($"  file   : {options.OutPath} ({FormatName(options.Format)})");
            }
        }

        stdout.WriteLine("\n=== merged transcript (speaker-attributed) ===");
        stdout.WriteLine(merged.ToString());
        return 0;
    }

    /// <summary>
    /// Runs the --vad model over the file and plans windows from the speech it
    /// found. Returns 0 on success, or the exit code the run should use.
    /// </summary>
    /// <remarks>
    /// Opt-in on purpose. Transcribing only the speech regions changes what the
    /// model sees (no context across a pause), so the text can differ from the
    /// normal path; the flag is documented as unmeasured rather than sold as an
    /// improvement.
    /// </remarks>
    private static int PlanFromSpeech(
        TextWriter stdout,
        TextWriter stderr,
        CliOptions options,
        DeviceChoice choice,
        PcmSource pcmSource,
        long totalSamples,
        int windowMs,
        out IReadOnlyList<AudioWindow> windows)
    {
        windows = [];

        string vadPath;
        try
        {
            vadPath = ModelStore.Resolve(options.VadSpec!, quant: null, stderr);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }

        using var vadModel = Model.Load(vadPath, p => p.WithBackend(choice.Backend));
        if (!vadModel.Roles.HasFlag(TranscribeRole.Vad))
        {
            stderr.WriteLine($"--vad: {vadModel.Architecture}/{vadModel.Variant} does not serve the VAD role "
                + $"(roles: {DescribeRoles(vadModel.Roles)}). Use a VAD model such as silero-vad-v6.2.");
            return 1;
        }

        using var vadSession = vadModel.CreateVadSession();
        VadWindowing.VadRunResult run = VadWindowing.Run(vadSession, pcmSource, options.ChunkSeconds);
        stdout.WriteLine(
            $"vad   : {vadModel.Architecture}/{vadModel.Variant}, "
            + $"{run.Segments.Count} speech interval(s), {run.FrameCount} frame(s)");

        windows = SpeechWindows.Plan(
            run.Segments.Select(s => new SpeechInterval(s.StartSample, s.EndSample)).ToArray(),
            totalSamples,
            windowMs);
        stdout.WriteLine($"windows: {windows.Count} speech window(s) of at most {windowMs / 1000}s (silence skipped)");

        if (windows.Count == 0)
        {
            stderr.WriteLine("--vad: the VAD model found no speech in this audio; nothing to transcribe.");
            return 1;
        }

        return 0;
    }

}
