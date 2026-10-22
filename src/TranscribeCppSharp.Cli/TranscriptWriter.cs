// The four --format shapes, split out of the top-level statements so the exact
// bytes of an exported transcript are unit tested: these files are consumed by
// other tools (WebVTT players, whisper-style JSON readers, word processors), and
// the shapes are documented in --help and in docs/exporting.md.

using System.Text.Json;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.SharedSource;

namespace TranscribeCppSharp.Cli;

/// <summary>
/// What every format says about the run: where the audio came from, which model
/// decoded it and when. Passed once to the header and once to the footer instead
/// of a growing argument list, so the HTML document and the plain header cannot
/// disagree about the same run.
/// </summary>
internal readonly record struct TranscriptInfo(
    string AudioPath,
    TimeSpan Duration,
    string ModelLabel,
    string Language,
    DiarizeMode Diarize,
    int WindowSeconds,
    DateTime Generated);

internal static class TranscriptWriter
{
    /// <summary>
    /// Writes what is known before the first window. JSON and HTML have nothing
    /// yet: those documents are written once at the end, when every segment is
    /// known.
    /// </summary>
    internal static void WriteHeader(TextWriter writer, TranscriptFormat format, TranscriptInfo info)
    {
        switch (format)
        {
            case TranscriptFormat.Vtt:
                WriteLine(writer, "WEBVTT");
                WriteLine(writer, $"NOTE source: {info.AudioPath}");
                WriteLine(writer,
                    $"NOTE audio {info.Duration:c} model {info.ModelLabel} lang {info.Language} "
                    + $"diarize {Fmt.DiarizeState(info.Diarize)} window {info.WindowSeconds}s generated {info.Generated:yyyy-MM-dd HH:mm:ss}");
                WriteLine(writer);
                break;

            case TranscriptFormat.Json:
            case TranscriptFormat.Html:
            case TranscriptFormat.Srt:
            case TranscriptFormat.Ass:
                break;

            default:
                WriteLine(writer, "# TranscribeCppSharp diarization transcript");
                WriteLine(writer, $"# source   : {info.AudioPath}");
                WriteLine(writer, $"# audio    : {info.Duration:c}");
                WriteLine(writer, $"# model    : {info.ModelLabel}");
                WriteLine(writer, $"# lang     : {info.Language}   diarize: {Fmt.DiarizeState(info.Diarize)}   window: {info.WindowSeconds}s");
                WriteLine(writer, $"# generated: {info.Generated:yyyy-MM-dd HH:mm:ss}");
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
            case TranscriptFormat.Html:
            case TranscriptFormat.Srt:
            case TranscriptFormat.Ass:
                // Those documents are written once, at the end.
                break;
        }
    }

    /// <summary>Writes what is known once every window has been transcribed.</summary>
    internal static void WriteFooter(
        TextWriter writer,
        TranscriptFormat format,
        TranscriptInfo info,
        IReadOnlyList<TranscriptLine> lines)
    {
        switch (format)
        {
            case TranscriptFormat.Json:
                WriteJson(writer, info.Language, lines);
                break;

            case TranscriptFormat.Html:
                HtmlTranscriptWriter.Write(
                    writer,
                    new HtmlTranscriptInfo(
                        info.AudioPath, info.Duration, info.ModelLabel, info.Language,
                        Fmt.DiarizeState(info.Diarize), info.WindowSeconds, info.Generated),
                    lines.Select(l => new HtmlTranscriptSegment(l.StartMs, l.EndMs, l.Speaker, l.Text)).ToArray());
                break;

            case TranscriptFormat.Srt:
            case TranscriptFormat.Ass:
                SubtitleWriter.Write(
                    writer,
                    format == TranscriptFormat.Srt ? SubtitleFormat.Srt : SubtitleFormat.Ass,
                    lines.Select(l => new SubtitleCue(l.StartMs, l.EndMs, l.Speaker, l.Text)).ToArray(),
                    speakerLabels: info.Diarize != DiarizeMode.DiarizeModeOff,
                    title: $"{Path.GetFileName(info.AudioPath)} — {info.ModelLabel}");
                break;

            case TranscriptFormat.Vtt:
                WriteLine(writer, $"NOTE done {info.Generated:yyyy-MM-dd HH:mm:ss}");
                break;

            default:
                WriteLine(writer);
                WriteLine(writer, $"# done: {info.Generated:yyyy-MM-dd HH:mm:ss}");
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
    /// passed in. WebVTT is conventionally LF-terminated, and the plain, JSON and
    /// HTML forms are only more consistent for being the same.
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
