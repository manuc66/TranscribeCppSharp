// Plain and JSON reports for the non-ASR roles (diarize, langid, vad). The ASR
// path keeps its own writer (TranscriptWriter): its formats are consumed by
// WebVTT players and whisper-style JSON readers, which these reports are not.
//
// --format vtt is refused for these roles before this runs (TranscribeCommand),
// so only plain and json reach here.

using System.Globalization;
using System.Text.Json;

namespace TranscribeCppSharp.Cli;

internal static class RoleReportWriter
{
    // One instance for the whole process: building JsonSerializerOptions reflects
    // over the serialized graph, which is why CA1869 flags a new one per call.
    // System.Text.Json freezes the instance on first use and documents it as safe
    // to use concurrently, so a single static field serves every report.
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static double Seconds(TimeSpan t) => Math.Round(t.TotalSeconds, 3);

    internal static void WriteDiarize(
        TextWriter w, TranscriptFormat format, string audioPath, string modelLabel,
        TimeSpan duration, IReadOnlyList<SpeakerSegmentResult> segments, DateTime generated)
    {
        if (format == TranscriptFormat.Json)
        {
            WriteJson(w, new
            {
                role = "diarize",
                source = audioPath,
                model = modelLabel,
                audio = Seconds(duration),
                segments = segments.Select(s => new { start = Seconds(s.Start), end = Seconds(s.End), speaker = s.SpeakerId }),
            });
            return;
        }

        Header(w, "diarization", audioPath, modelLabel, duration, generated);
        foreach (var s in segments)
        {
            Line(w, $"[{Fmt.Ts(s.Start.TotalMilliseconds)} -> {Fmt.Ts(s.End.TotalMilliseconds)}] Speaker {s.SpeakerId}");
        }

        Footer(w, generated);
    }

    internal static void WriteLangId(
        TextWriter w, TranscriptFormat format, string audioPath, string modelLabel,
        TimeSpan duration, LangIdSession.LangIdResult result, DateTime generated)
    {
        if (format == TranscriptFormat.Json)
        {
            WriteJson(w, new
            {
                role = "langid",
                source = audioPath,
                model = modelLabel,
                audio = Seconds(duration),
                allowed_mass = result.AllowedMass,
                candidates = result.Candidates.Select(c => new { code = c.Code, name = c.Name, probability = c.Probability }),
            });
            return;
        }

        Header(w, "language identification", audioPath, modelLabel, duration, generated);
        foreach (var c in result.Candidates)
        {
            Line(w, string.Create(CultureInfo.InvariantCulture, $"{c.Code,-8} {c.Probability:0.000}  {c.Name}"));
        }

        Line(w);
        Line(w, string.Create(CultureInfo.InvariantCulture, $"# allowed mass: {result.AllowedMass:0.000}"));
        Footer(w, generated);
    }

    internal static void WriteVad(
        TextWriter w, TranscriptFormat format, string audioPath, string modelLabel,
        TimeSpan duration, IReadOnlyList<VadSession.VadSegmentResult> segments, int frameCount, int sampleRate, DateTime generated)
    {
        static double Ms(long samples, int rate) => Math.Round(samples * 1000.0 / rate, 1);

        if (format == TranscriptFormat.Json)
        {
            WriteJson(w, new
            {
                role = "vad",
                source = audioPath,
                model = modelLabel,
                audio = Seconds(duration),
                sample_rate = sampleRate,
                frames = frameCount,
                segments = segments.Select(s => new
                {
                    start_sample = s.StartSample,
                    end_sample = s.EndSample,
                    start = Math.Round(s.StartSample * 1000.0 / sampleRate, 3),
                    end = Math.Round(s.EndSample * 1000.0 / sampleRate, 3),
                }),
            });
            return;
        }

        Header(w, "speech activity", audioPath, modelLabel, duration, generated);
        Line(w, $"# frames: {frameCount}");
        foreach (var s in segments)
        {
            Line(w, $"[{Fmt.Ts(Ms(s.StartSample, sampleRate))} -> {Fmt.Ts(Ms(s.EndSample, sampleRate))}] speech");
        }

        Footer(w, generated);
    }

    private static void Header(TextWriter w, string kind, string audioPath, string modelLabel, TimeSpan duration, DateTime generated)
    {
        Line(w, $"# TranscribeCppSharp {kind} report");
        Line(w, $"# source   : {audioPath}");
        Line(w, $"# model    : {modelLabel}");
        Line(w, $"# audio    : {duration:c}");
        Line(w, $"# generated: {generated:yyyy-MM-dd HH:mm:ss}");
        Line(w);
    }

    private static void Footer(TextWriter w, DateTime generated)
    {
        Line(w);
        Line(w, $"# done: {generated:yyyy-MM-dd HH:mm:ss}");
    }

    private static void WriteJson(TextWriter w, object document)
        => Line(w, JsonSerializer.Serialize(document, JsonOptions));

    // LF-terminated regardless of the writer's convention, like TranscriptWriter:
    // the same audio has to produce the same bytes on every platform.
    private static void Line(TextWriter w, string line)
    {
        w.Write(line);
        w.Write('\n');
    }

    private static void Line(TextWriter w) => Line(w, string.Empty);
}
