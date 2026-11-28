namespace TranscribeCppSharp.MeetingAnalytics;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>Markdown exporter for anonymous meeting analytics.</summary>
public static class MeetingAnalyticsMarkdown
{
    /// <summary>Writes a readable summary; no `PersonId`, no identity, no `nl`.</summary>
    public static string Write(MeetingAnalytics analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);

        var builder = new StringBuilder();
        builder.AppendLine("# Meeting speaker analytics");
        builder.AppendLine();
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Duration: {Format(analytics.Summary.Duration)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Speakers: {analytics.Summary.SpeakerCount.ToString(CultureInfo.InvariantCulture)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Attributed speech: {Format(analytics.Summary.TotalSpeechTime)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Silence: {Format(analytics.Summary.TotalSilenceTime)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Overlap: {Format(analytics.Summary.TotalOverlapTime)}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Speaker changes: {analytics.Summary.SpeakerChangeCount.ToString(CultureInfo.InvariantCulture)}"));
        builder.AppendLine();
        builder.AppendLine("| Speaker | Talk time | Share | Turns | Words | Words/min |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");
        Dictionary<int, MeetingSpeakerLabel> labels = analytics.SpeakerLabels.ToDictionary(l => l.SpeakerId);

        foreach (SpeakerTalkStats speaker in analytics.Speakers)
        {
            MeetingSpeakerLabel? label = labels.GetValueOrDefault(speaker.SpeakerId);
            string name = string.IsNullOrWhiteSpace(label?.DisplayName)
                ? $"Speaker {speaker.SpeakerId.ToString(CultureInfo.InvariantCulture)}"
                : label.DisplayName.Trim();
            builder.Append("| ").Append(name.Replace("|", "\\|", StringComparison.Ordinal))
                .Append(" | ").Append(Format(speaker.TalkTime))
                .Append(" | ").Append(speaker.TalkShare.ToString("0.0%", CultureInfo.InvariantCulture))
                .Append(" | ").Append(speaker.TurnCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(speaker.WordCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(speaker.WordsPerMinute.ToString("0.0", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        return builder.ToString();
    }

    private static string Format(TimeSpan value)
        => value.ToString("c", CultureInfo.InvariantCulture);
}
