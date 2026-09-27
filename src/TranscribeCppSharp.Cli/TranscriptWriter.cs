// The three --format shapes, split out of the top-level statements so the exact
// bytes of an exported transcript are unit tested: these files are consumed by
// other tools (WebVTT players, whisper-style JSON readers) and the shapes are
// documented in --help and in the README.

using System.Text.Json;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Cli;

internal static class TranscriptWriter
{
    /// <summary>
    /// Writes what is known before the first window. JSON has nothing yet: the
    /// document is written once at the end, when every segment is known.
    /// </summary>
    internal static void WriteHeader(
        TextWriter writer,
        TranscriptFormat format,
        string audioPath,
        TimeSpan duration,
        string modelLabel,
        string language,
        DiarizeMode diarize,
        int windowSeconds,
        DateTime generated)
    {
        switch (format)
        {
            case TranscriptFormat.Vtt:
                writer.WriteLine("WEBVTT");
                writer.WriteLine($"NOTE source: {audioPath}");
                writer.WriteLine(
                    $"NOTE audio {duration:c} model {modelLabel} lang {language} diarize {Fmt.DiarizeState(diarize)} window {windowSeconds}s generated {generated:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine();
                break;

            case TranscriptFormat.Json:
                break;

            default:
                writer.WriteLine("# TranscribeCppSharp diarization transcript");
                writer.WriteLine($"# source   : {audioPath}");
                writer.WriteLine($"# audio    : {duration:c}");
                writer.WriteLine($"# model    : {modelLabel}");
                writer.WriteLine($"# lang     : {language}   diarize: {Fmt.DiarizeState(diarize)}   window: {windowSeconds}s");
                writer.WriteLine($"# generated: {generated:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine();
                break;
        }
    }

    /// <summary>Writes one segment, in the shape of the chosen format.</summary>
    internal static void WriteSegment(TextWriter writer, TranscriptFormat format, TranscriptLine line)
    {
        switch (format)
        {
            case TranscriptFormat.Vtt:
                writer.WriteLine($"{Fmt.VttTs(line.StartMs)} --> {Fmt.VttTs(line.EndMs)}");
                writer.WriteLine($"<v Speaker {line.Speaker}>{line.Text}</v>");
                writer.WriteLine();
                break;

            case TranscriptFormat.Plain:
                writer.WriteLine($"[{Fmt.Ts(line.StartMs)} -> {Fmt.Ts(line.EndMs)}] Speaker {line.Speaker}: {line.Text}");
                break;

            case TranscriptFormat.Json:
                // The JSON document is written once, at the end.
                break;
        }
    }

    /// <summary>Writes what is known once every window has been transcribed.</summary>
    internal static void WriteFooter(TextWriter writer, TranscriptFormat format, string language, IReadOnlyList<TranscriptLine> lines, DateTime generated)
    {
        switch (format)
        {
            case TranscriptFormat.Json:
                WriteJson(writer, language, lines);
                break;

            case TranscriptFormat.Vtt:
                writer.WriteLine($"NOTE done {generated:yyyy-MM-dd HH:mm:ss}");
                break;

            default:
                writer.WriteLine();
                writer.WriteLine($"# done: {generated:yyyy-MM-dd HH:mm:ss}");
                break;
        }
    }

    private static void WriteJson(TextWriter writer, string language, IReadOnlyList<TranscriptLine> lines)
    {
        // The numbers go through JsonSerializer, which always writes them with a
        // '.' decimal separator: a locale that uses ',' would otherwise produce
        // a file no JSON reader accepts.
        var fullText = string.Join(' ', lines.Select(l => l.Text));
        writer.WriteLine("{");
        writer.WriteLine($"  \"language\": {JsonSerializer.Serialize(language)},");
        writer.WriteLine($"  \"text\": {JsonSerializer.Serialize(fullText)},");
        writer.WriteLine("  \"segments\": [");
        for (int i = 0; i < lines.Count; i++)
        {
            TranscriptLine line = lines[i];
            string comma = i < lines.Count - 1 ? "," : string.Empty;
            writer.WriteLine($"    {JsonSerializer.Serialize(new
            {
                start = Math.Round(line.StartMs / 1000.0, 3),
                end = Math.Round(line.EndMs / 1000.0, 3),
                speaker = line.Speaker,
                text = line.Text,
            })}{comma}");
        }

        writer.WriteLine("  ]");
        writer.WriteLine("}");
    }
}
