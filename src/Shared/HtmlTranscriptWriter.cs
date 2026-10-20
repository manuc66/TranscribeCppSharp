// Renders a transcript as a standalone HTML document: numbered paragraphs,
// timestamps and speaker labels, readable in a browser and importable by a word
// processor (the layout is a table with literal line numbers, which is what
// survives Word's HTML import; CSS counters do not).
//
// Shared source: compiled into both the CLI and the desktop app (see the csproj
// Compile includes), like UpstreamCatalog and VadWindowing. The two front ends
// must produce the same document from the same transcript, so there is one
// renderer rather than one per front end.
//
// Nothing here is claimed about accuracy: the document says which model, which
// language and when, and nothing about how good the result is.

using System.Globalization;
using System.Net;

namespace TranscribeCppSharp.SharedSource;

/// <summary>One transcript paragraph, positioned by its start on the timeline.</summary>
public readonly record struct HtmlTranscriptSegment(double StartMs, int Speaker, string Text);

/// <summary>What the document records about the run that produced it.</summary>
public readonly record struct HtmlTranscriptInfo(
    string SourcePath,
    TimeSpan? Duration,
    string ModelLabel,
    string Language,
    string Diarize,
    int WindowSeconds,
    DateTime Generated);

/// <summary>
/// Writes the HTML transcript. The shape is documented in docs/exporting.md:
/// one table row per segment, a line number in the first cell, and timestamps
/// inserted at the start, on every speaker change and every minute, so a reader
/// can paste the text into a qualitative-analysis tool and keep the positions.
/// </summary>
public static class HtmlTranscriptWriter
{
    /// <summary>Longest gap the inline timestamps allow, in milliseconds.</summary>
    public const int TimestampIntervalMs = 60_000;

    /// <summary>Writes the whole document, from &lt;!DOCTYPE html&gt; to &lt;/html&gt;.</summary>
    public static void Write(
        TextWriter writer,
        HtmlTranscriptInfo info,
        IReadOnlyList<HtmlTranscriptSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(segments);

        string title = Path.GetFileName(info.SourcePath);
        if (string.IsNullOrEmpty(title))
        {
            title = info.SourcePath;
        }

        WriteLine(writer, "<!DOCTYPE html>");
        WriteLine(writer, $"<html{LangAttribute(info.Language)}>");
        WriteLine(writer, "<head>");
        WriteLine(writer, "<meta charset=\"utf-8\">");
        WriteLine(writer, "<meta name=\"generator\" content=\"TranscribeCppSharp\">");
        WriteLine(writer, $"<title>{Encode(title)} — transcript</title>");
        WriteLine(writer, "<style>");
        WriteLine(writer, "body { font-family: Georgia, \"Times New Roman\", serif; font-size: 11pt; color: #1b1b1b; margin: 2em; }");
        WriteLine(writer, "h1 { font-size: 15pt; margin: 0 0 0.25em 0; }");
        WriteLine(writer, "p.meta { font-family: Arial, Helvetica, sans-serif; font-size: 9pt; color: #555; margin: 0 0 1.5em 0; line-height: 1.5; }");
        WriteLine(writer, "table.transcript { border-collapse: collapse; width: 100%; }");
        WriteLine(writer, "table.transcript td { vertical-align: top; padding: 0.15em 0.5em 0.6em 0; }");
        WriteLine(writer, "td.ln { width: 3.5em; text-align: right; color: #888; font-family: Arial, Helvetica, sans-serif; font-size: 8.5pt; }");
        WriteLine(writer, "td.text { text-align: justify; line-height: 1.45; }");
        WriteLine(writer, "span.ts { color: #555; font-size: 9pt; white-space: nowrap; }");
        WriteLine(writer, "span.speaker { font-weight: bold; }");
        WriteLine(writer, "tr { page-break-inside: avoid; }");
        WriteLine(writer, "</style>");
        WriteLine(writer, "</head>");
        WriteLine(writer, "<body>");
        WriteLine(writer, $"<h1>{Encode(title)}</h1>");
        WriteLine(writer, "<p class=\"meta\">");
        WriteLine(writer, $"source: {Encode(info.SourcePath)}<br>");
        WriteLine(writer,
            $"audio: {FormatDuration(info.Duration)} · model {Encode(info.ModelLabel)} · "
            + $"language {Encode(info.Language)} · diarize {Encode(info.Diarize)} · "
            + $"window {info.WindowSeconds}s · segments {segments.Count}<br>");
        WriteLine(writer, $"generated: {FormatGenerated(info.Generated)}");
        WriteLine(writer, "</p>");

        if (segments.Count == 0)
        {
            WriteLine(writer, "<p class=\"empty\">No transcript segments were produced.</p>");
        }
        else
        {
            WriteLine(writer, "<table class=\"transcript\">");
            double lastStampMs = double.NegativeInfinity;
            int lastSpeaker = int.MinValue;
            for (int i = 0; i < segments.Count; i++)
            {
                HtmlTranscriptSegment segment = segments[i];

                // A timestamp at the start, on a speaker change, and every minute
                // at most. The speaker change resets the interval so the label
                // that follows never looks like it belongs to an older turn.
                bool timestamp = i == 0
                    || segment.Speaker != lastSpeaker
                    || segment.StartMs - lastStampMs >= TimestampIntervalMs;
                if (timestamp)
                {
                    lastStampMs = segment.StartMs;
                }

                lastSpeaker = segment.Speaker;

                var cell = new System.Text.StringBuilder();
                if (timestamp)
                {
                    cell.Append("<span class=\"ts\">[").Append(FormatClock(segment.StartMs)).Append("]</span> ");
                }

                cell.Append("<span class=\"speaker\">Speaker ").Append(segment.Speaker).Append(":</span> ");
                cell.Append(Encode(segment.Text));

                WriteLine(writer, $"<tr><td class=\"ln\">{i + 1}</td><td class=\"text\">{cell}</td></tr>");
            }

            WriteLine(writer, "</table>");
        }

        WriteLine(writer, "</body>");
        WriteLine(writer, "</html>");
    }

    /// <summary>
    /// The lang attribute, or nothing when the value is not a language tag. The
    /// CLI uses "auto" when the model chooses for itself, which is not a tag.
    /// </summary>
    private static string LangAttribute(string language)
        => IsLanguageTag(language) ? $" lang=\"{Encode(language)}\"" : string.Empty;

    private static bool IsLanguageTag(string language)
    {
        // "auto" is the CLI's sentinel for "the model chooses", not a language.
        if (string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrEmpty(language) || !char.IsAsciiLetter(language[0]) || !char.IsAsciiLetter(language[^1]))
        {
            return false;
        }

        foreach (char c in language)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>HH:MM:SS, always with hours, so every stamp in a file aligns.</summary>
    private static string FormatClock(double totalMs)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, totalMs));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>
    /// The duration as the plain and VTT headers print it: 00:11 or 1:02:03. The
    /// desktop app does not keep the file's duration, so it passes null and the
    /// document says "unknown" rather than measuring the last segment and calling
    /// that the audio length.
    /// </summary>
    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } value)
        {
            return "unknown";
        }

        return value.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{value.Minutes:00}:{value.Seconds:00}");
    }

    /// <summary>
    /// The generation time in the same format the other formats use, invariant so
    /// the document does not depend on the machine's culture.
    /// </summary>
    private static string FormatGenerated(DateTime generated)
        => generated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes text for HTML. Forgetting this is how a transcript containing
    /// "&lt;" or "&amp;" becomes a document that no longer renders what was said.
    /// </summary>
    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>Writes one "\n"-terminated line, whatever the writer's newline is.</summary>
    private static void WriteLine(TextWriter writer, string line)
    {
        writer.Write(line);
        writer.Write('\n');
    }
}
