// Renders a merged transcript as subtitles: SubRip (.srt), WebVTT (.vtt) and
// Advanced SubStation Alpha (.ass).
//
// Shared source: compiled into the CLI, the server and the desktop app, like
// HtmlTranscriptWriter. The point is that the same transcript produces the same
// bytes wherever it is exported from, so there is one renderer rather than one
// per front end.
//
// Nothing here invents timing. A cue is a segment the model produced; when the
// segment list is empty the file is empty too, rather than carrying fabricated
// cues. Speaker labels are opt-in because a model that cannot attribute speakers
// reports every segment as Speaker 0, and "Speaker 0:" on every line is noise,
// not information.

using System.Globalization;
using System.Text;

namespace TranscribeCppSharp.SharedSource;

/// <summary>The subtitle formats this renderer writes.</summary>
public enum SubtitleFormat
{
    /// <summary>SubRip: numbered blocks, <c>HH:MM:SS,mmm</c> timestamps.</summary>
    Srt,

    /// <summary>WebVTT: a <c>WEBVTT</c> header, <c>HH:MM:SS.mmm</c> timestamps, voice tags for speakers.</summary>
    Vtt,

    /// <summary>Advanced SubStation Alpha: one script with a Default style.</summary>
    Ass,
}

/// <summary>One subtitle cue, positioned against the whole file.</summary>
/// <param name="StartMs">Start in milliseconds.</param>
/// <param name="EndMs">End in milliseconds.</param>
/// <param name="Speaker">Speaker index; ignored unless the caller asks for labels.</param>
/// <param name="Text">What was said.</param>
public readonly record struct SubtitleCue(double StartMs, double EndMs, int Speaker, string Text);

/// <summary>Writes cues as SubRip, WebVTT or ASS.</summary>
public static class SubtitleWriter
{
    /// <summary>
    /// Writes <paramref name="cues"/> in the requested format.
    /// </summary>
    /// <param name="writer">Destination.</param>
    /// <param name="format">Which subtitle format.</param>
    /// <param name="cues">The segments to write, in order.</param>
    /// <param name="speakerLabels">
    /// Whether to prefix each cue with its speaker. Pass false when attribution
    /// did not run: every cue would otherwise say "Speaker 0".
    /// </param>
    /// <param name="title">What the document says about itself, when the format can carry it.</param>
    /// <param name="speakerNames">
    /// Names the reader typed for a speaker id. A missing entry falls back to
    /// "Speaker N", which is what the transcript itself reports.
    /// </param>
    public static void Write(
        TextWriter writer,
        SubtitleFormat format,
        IEnumerable<SubtitleCue> cues,
        bool speakerLabels,
        string? title = null,
        IReadOnlyDictionary<int, string>? speakerNames = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(cues);

        switch (format)
        {
            case SubtitleFormat.Srt:
                WriteSrt(writer, cues, speakerLabels, speakerNames);
                break;
            case SubtitleFormat.Vtt:
                WriteVtt(writer, cues, speakerLabels, title, speakerNames);
                break;
            case SubtitleFormat.Ass:
                WriteAss(writer, cues, speakerLabels, title, speakerNames);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "unknown subtitle format");
        }
    }

    /// <summary>The file extension a format conventionally uses, without the dot.</summary>
    public static string Extension(SubtitleFormat format) => format switch
    {
        SubtitleFormat.Srt => "srt",
        SubtitleFormat.Vtt => "vtt",
        SubtitleFormat.Ass => "ass",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unknown subtitle format"),
    };

    private static void WriteSrt(
        TextWriter writer,
        IEnumerable<SubtitleCue> cues,
        bool speakerLabels,
        IReadOnlyDictionary<int, string>? speakerNames)
    {
        int index = 0;
        foreach (SubtitleCue cue in cues)
        {
            string text = Normalize(SingleLine(cue.Text));
            if (text.Length == 0)
            {
                continue;
            }

            index++;
            WriteLine(writer, index.ToString(CultureInfo.InvariantCulture));
            WriteLine(writer, $"{SrtTime(cue.StartMs)} --> {SrtTime(cue.EndMs)}");
            WriteLine(writer, speakerLabels ? Prefix(cue.Speaker, text, speakerNames) : text);
            WriteLine(writer);
        }
    }

    private static void WriteVtt(
        TextWriter writer,
        IEnumerable<SubtitleCue> cues,
        bool speakerLabels,
        string? title,
        IReadOnlyDictionary<int, string>? speakerNames)
    {
        WriteLine(writer, "WEBVTT");
        if (!string.IsNullOrWhiteSpace(title))
        {
            WriteLine(writer, $"NOTE {SingleLine(title)}");
        }

        WriteLine(writer);

        foreach (SubtitleCue cue in cues)
        {
            string text = Normalize(SingleLine(cue.Text));
            if (text.Length == 0)
            {
                continue;
            }

            WriteLine(writer, $"{VttTime(cue.StartMs)} --> {VttTime(cue.EndMs)}");
            WriteLine(writer, speakerLabels
                ? $"<v {Name(cue.Speaker, speakerNames)}>{EscapeVtt(text)}</v>"
                : EscapeVtt(text));
            WriteLine(writer);
        }
    }

    private static void WriteAss(
        TextWriter writer,
        IEnumerable<SubtitleCue> cues,
        bool speakerLabels,
        string? title,
        IReadOnlyDictionary<int, string>? speakerNames)
    {
        WriteLine(writer, "[Script Info]");
        WriteLine(writer, $"Title: {SingleLine(title ?? "TranscribeCppSharp transcript")}");
        WriteLine(writer, "ScriptType: v4.00+");
        WriteLine(writer, "WrapStyle: 0");
        WriteLine(writer, "ScaledBorderAndShadow: yes");
        WriteLine(writer, "PlayResX: 1920");
        WriteLine(writer, "PlayResY: 1080");
        WriteLine(writer);
        WriteLine(writer, "[V4+ Styles]");
        WriteLine(writer,
            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, "
            + "Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, "
            + "Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        WriteLine(writer,
            "Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,"
            + "0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1");
        WriteLine(writer);
        WriteLine(writer, "[Events]");
        WriteLine(writer, "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (SubtitleCue cue in cues)
        {
            string text = Normalize(cue.Text);
            if (text.Length == 0)
            {
                continue;
            }

            string dialogue = speakerLabels ? Prefix(cue.Speaker, text, speakerNames) : text;
            dialogue = EscapeAss(dialogue).Replace("\n", "\\N", StringComparison.Ordinal);
            WriteLine(writer,
                $"Dialogue: 0,{AssTime(cue.StartMs)},{AssTime(cue.EndMs)},Default,,0,0,0,,{dialogue}");
        }
    }

    private static string Prefix(int speaker, string text, IReadOnlyDictionary<int, string>? names)
        => $"{Name(speaker, names)}: {text}";

    private static string Name(int speaker, IReadOnlyDictionary<int, string>? names)
        => names is not null && names.TryGetValue(speaker, out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : $"Speaker {speaker.ToString(CultureInfo.InvariantCulture)}";

    private static string SingleLine(string text)
        => text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');

    /// <summary>Collapses runs of whitespace, which ASR segments often carry.</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string EscapeVtt(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EscapeAss(string text)
        => text.Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal);

    private static string SrtTime(double milliseconds)
    {
        (long hours, long minutes, long seconds, long ms) = Split(milliseconds);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{minutes:00}:{seconds:00},{ms:000}");
    }

    private static string VttTime(double milliseconds)
    {
        (long hours, long minutes, long seconds, long ms) = Split(milliseconds);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{minutes:00}:{seconds:00}.{ms:000}");
    }

    private static string AssTime(double milliseconds)
    {
        (long hours, long minutes, long seconds, long ms) = Split(milliseconds);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{minutes:00}:{seconds:00}.{ms / 10:00}");
    }

    private static (long Hours, long Minutes, long Seconds, long Milliseconds) Split(double milliseconds)
    {
        // Negative or NaN input cannot produce a valid cue; clamp it rather than
        // emitting a timestamp no parser accepts.
        long total = double.IsNaN(milliseconds) || milliseconds < 0
            ? 0
            : (long)Math.Round(milliseconds, MidpointRounding.AwayFromZero);

        return (total / 3_600_000, total / 60_000 % 60, total / 1000 % 60, total % 1000);
    }

    private static void WriteLine(TextWriter writer, string line)
    {
        writer.Write(line);
        writer.Write('\n');
    }

    private static void WriteLine(TextWriter writer) => WriteLine(writer, string.Empty);
}
