using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using Avalonia.Input.Platform;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class TranscriptionViewModel
{
    /// <summary>
    /// The subtitle exports, through the same renderer the CLI uses. Speaker
    /// names typed in the tab are used when present; otherwise the cue says
    /// "Speaker N", as the WebVTT export does.
    /// </summary>
    /// <remarks>
    /// The cues are the segments as the model returned them: nothing is re-timed
    /// or split to make them look shorter.
    /// </remarks>
    internal string BuildSubtitleExport(SubtitleFormat format)
    {
        SubtitleCue[] cues = Segments
            .Select(s => new SubtitleCue(s.Start.TotalMilliseconds, s.End.TotalMilliseconds, s.SpeakerId, s.Text))
            .ToArray();

        var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        SubtitleWriter.Write(
            writer,
            format,
            cues,
            speakerLabels: true,
            title: string.IsNullOrEmpty(AudioPath) ? "Transcript" : Path.GetFileName(AudioPath),
            speakerNames: NamedSpeakers());
        return writer.ToString();
    }

    private string ExportAsJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(Result, JsonOptions);
    }

    /// <summary>
    /// The same document the CLI's --format html writes, through the shared
    /// renderer. The duration is left unknown: the app does not keep the audio
    /// file's length, and the last segment's end is not it.
    /// </summary>
    /// <remarks>
    /// The three export options are read here, not stored in the result: the same
    /// transcript exported twice can differ only in the document that describes
    /// it. The transcript itself is never rewritten.
    /// </remarks>
    internal string BuildHtmlExport()
    {
        ResultProvenance provenance = Provenance();
        var info = new TranscribeCppSharp.SharedSource.HtmlTranscriptInfo(
            AudioPath,
            Duration: Result?.Duration,
            provenance.ModelAlias,
            provenance.Language,
            provenance.Diarize,
            provenance.WindowSeconds,
            DateTime.Now);

        var segments = Segments
            .Select(s => new TranscribeCppSharp.SharedSource.HtmlTranscriptSegment(
                s.Start.TotalMilliseconds, s.End.TotalMilliseconds, s.SpeakerId, s.Text))
            .ToArray();

        Dictionary<int, string> names = NamedSpeakers();
        using var writer = new StringWriter();
        TranscribeCppSharp.SharedSource.HtmlTranscriptWriter.Write(
            writer,
            info,
            segments,
            timestampIntervalMs: Math.Max(0, TimestampIntervalSeconds) * 1000,
            pauseThresholdMs: MarkPauses ? Math.Max(1, PauseThresholdSeconds) * 1000 : 0,
            speakerNames: names.Count > 0 ? names : null);
        return writer.ToString();
    }

    /// <summary>
    /// A Markdown note for the same result: a header, then one paragraph per
    /// segment with the speaker in bold and the timestamp in code, plus the pause
    /// markers when they are on.
    /// </summary>
    /// <remarks>
    /// The text is escaped so a transcript containing <c>*</c>, <c>_</c> or angle
    /// brackets renders as what was said rather than as markup. This is a note,
    /// not another transcript format: there is no claim that a Markdown reader
    /// aligns the times, and the timestamps follow the same setting as the HTML
    /// export.
    /// </remarks>
    internal string BuildMarkdownExport()
    {
        Dictionary<int, string> names = NamedSpeakers();
        string title = string.IsNullOrEmpty(AudioPath) ? "Transcript" : Path.GetFileName(AudioPath);
        ResultProvenance provenance = Provenance();

        var sb = new System.Text.StringBuilder();
        sb.Append("# ").Append(EscapeMarkdown(title)).Append("\n\n");
        sb.Append("- **Source:** ").Append(CodeSpan(AudioPath)).Append('\n');
        sb.Append("- **Model:** ").Append(CodeSpan(provenance.ModelAlias)).Append('\n');
        sb.Append("- **Language:** ").Append(CodeSpan(provenance.Language)).Append('\n');
        sb.Append("- **Diarization:** ").Append(provenance.Diarize).Append('\n');
        sb.Append("- **Window:** ").Append(provenance.WindowSeconds).Append(" s\n");
        sb.Append("- **Generated:** ")
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))
            .Append("\n\n");

        double lastStampMs = double.NegativeInfinity;
        int lastSpeaker = int.MinValue;
        for (int i = 0; i < Segments.Count; i++)
        {
            MergedSegment segment = Segments[i];

            if (MarkPauses && i > 0)
            {
                double gapMs = (segment.Start - Segments[i - 1].End).TotalMilliseconds;
                if (gapMs >= Math.Max(1, PauseThresholdSeconds) * 1000)
                {
                    int dots = Math.Clamp((int)Math.Floor(gapMs / 1000.0), 1, 10);
                    sb.Append("*[")
                      .Append(new string('.', dots))
                      .Append("]*\n\n");
                }
            }

            double startMs = segment.Start.TotalMilliseconds;
            bool timestamp = i == 0
                || segment.SpeakerId != lastSpeaker
                || (TimestampIntervalSeconds > 0 && startMs - lastStampMs >= TimestampIntervalSeconds * 1000);
            if (timestamp)
            {
                lastStampMs = startMs;
            }

            lastSpeaker = segment.SpeakerId;

            string speaker = names.TryGetValue(segment.SpeakerId, out string? name)
                ? name
                : $"Speaker {segment.SpeakerId}";
            sb.Append("**").Append(EscapeMarkdown(speaker)).Append("**");
            if (timestamp)
            {
                sb.Append(" `[").Append(FormatClock(segment.Start)).Append("]`");
            }

            sb.Append(" — ").Append(EscapeMarkdown(segment.Text)).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>The language the result reports, or the requested one when it reports none.</summary>
    private string EffectiveLanguage()
        => string.IsNullOrEmpty(Result?.DetectedLanguage) ? Language : Result!.DetectedLanguage;

    /// <summary>How the run's diarization request is described in the exports.</summary>
    private string DiarizeLabel() => DiarizeLabel(this.DiarizeMode);

    private static string DiarizeLabel(TranscribeCppSharp.Interop.DiarizeMode mode)
        => mode switch
        {
            TranscribeCppSharp.Interop.DiarizeMode.DiarizeModeOff => "off",
            TranscribeCppSharp.Interop.DiarizeMode.DiarizeModeOn => "on",
            _ => "model default",
        };

    /// <summary>HH:MM:SS, always with hours, as the HTML renderer writes it.</summary>
    private static string FormatClock(TimeSpan time)
        => $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";

    /// <summary>
    /// A backtick code span that survives a value containing backticks.
    /// </summary>
    /// <remarks>
    /// A backslash does not escape anything inside a code span, so the value
    /// needs a fence longer than the longest run of backticks it contains. A
    /// file name is user input: "notes` $(rm -rf).wav" must stay one value.
    /// </remarks>
    /// <param name="value">The value to show verbatim.</param>
    /// <returns>The Markdown code span.</returns>
    private static string CodeSpan(string value)
    {
        string flat = value.Replace('\n', ' ').Replace('\r', ' ');
        int longest = 0;
        int run = 0;
        foreach (char c in flat)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        string fence = new('`', longest + 1);
        return longest == 0
            ? $"{fence}{flat}{fence}"
            : $"{fence} {flat} {fence}";
    }

    /// <summary>
    /// Escapes the characters that would turn a sentence into markup, and turns
    /// a line break into a space so one segment stays one paragraph.
    /// </summary>
    private static string EscapeMarkdown(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>')
            {
                sb.Append('\\');
            }

            sb.Append(c is '\n' or '\r' ? ' ' : c);
        }

        return sb.ToString();
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }
}
