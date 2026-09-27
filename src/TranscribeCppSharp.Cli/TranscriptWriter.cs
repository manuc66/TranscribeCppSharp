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
                WriteLine(writer, "WEBVTT");
                WriteLine(writer, $"NOTE source: {audioPath}");
                WriteLine(writer, 
                    $"NOTE audio {duration:c} model {modelLabel} lang {language} diarize {Fmt.DiarizeState(diarize)} window {windowSeconds}s generated {generated:yyyy-MM-dd HH:mm:ss}");
                WriteLine(writer);
                break;

            case TranscriptFormat.Json:
                break;

            default:
                WriteLine(writer, "# TranscribeCppSharp diarization transcript");
                WriteLine(writer, $"# source   : {audioPath}");
                WriteLine(writer, $"# audio    : {duration:c}");
                WriteLine(writer, $"# model    : {modelLabel}");
                WriteLine(writer, $"# lang     : {language}   diarize: {Fmt.DiarizeState(diarize)}   window: {windowSeconds}s");
                WriteLine(writer, $"# generated: {generated:yyyy-MM-dd HH:mm:ss}");
                WriteLine(writer);
                break;
        }
    }

    /// <summary>Writes one segment, in the shape of the chosen format.</summary>
    internal static void WriteSegment(TextWriter writer, TranscriptFormat format, TranscriptLine line)
    {
        switch (format)
        {
            case TranscriptFormat.Vtt:
                WriteLine(writer, $"{Fmt.VttTs(line.StartMs)} --> {Fmt.VttTs(line.EndMs)}");
                WriteLine(writer, $"<v Speaker {line.Speaker}>{line.Text}</v>");
                WriteLine(writer);
                break;

            case TranscriptFormat.Plain:
                WriteLine(writer, $"[{Fmt.Ts(line.StartMs)} -> {Fmt.Ts(line.EndMs)}] Speaker {line.Speaker}: {line.Text}");
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
                WriteLine(writer, $"NOTE done {generated:yyyy-MM-dd HH:mm:ss}");
                break;

            default:
                WriteLine(writer);
                WriteLine(writer, $"# done: {generated:yyyy-MM-dd HH:mm:ss}");
                break;
        }
    }

    private static void WriteJson(TextWriter writer, string language, IReadOnlyList<TranscriptLine> lines)
    {
        // The numbers go through JsonSerializer, which always writes them with a
        // '.' decimal separator: a locale that uses ',' would otherwise produce
        // a file no JSON reader accepts.
        var fullText = string.Join(' ', lines.Select(l => l.Text));
        WriteLine(writer, "{");
        WriteLine(writer, $"  \"language\": {JsonSerializer.Serialize(language)},");
        WriteLine(writer, $"  \"text\": {JsonSerializer.Serialize(fullText)},");
        WriteLine(writer, "  \"segments\": [");
        for (int i = 0; i < lines.Count; i++)
        {
            TranscriptLine line = lines[i];
            string comma = i < lines.Count - 1 ? "," : string.Empty;
            WriteLine(writer, $"    {JsonSerializer.Serialize(new
            {
                start = Math.Round(line.StartMs / 1000.0, 3),
                end = Math.Round(line.EndMs / 1000.0, 3),
                speaker = line.Speaker,
                text = line.Text,
            })}{comma}");
        }

        WriteLine(writer, "  ]");
        WriteLine(writer, "}");
    }

    /// <summary>
    /// Writes one line terminated by "\n", whatever the writer's own newline
    /// convention is.
    /// </summary>
    /// <remarks>
    /// The transcript formats are files other tools read, and the same audio has
    /// to produce the same bytes on every platform. <see cref="TextWriter"/>
    /// .WriteLine emits "\r\n" on Windows, so relying on it made a WebVTT file
    /// written there differ byte-for-byte from the identical file written on
    /// Linux, and made the output depend on whichever writer happened to be
    /// passed in. WebVTT is conventionally LF-terminated, and the plain and JSON
    /// forms are only more consistent for being the same.
    /// </remarks>
    private static void WriteLine(TextWriter writer, string line)
    {
        writer.Write(line);
        writer.Write('\n');
    }

    /// <summary>Writes an empty line.</summary>
    private static void WriteLine(TextWriter writer)
        => WriteLine(writer, string.Empty);
}
