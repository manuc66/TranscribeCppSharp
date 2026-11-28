namespace TranscribeCppSharp.MeetingAnalytics;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>CSV exporter for per-speaker anonymous statistics.</summary>
public static class MeetingAnalyticsCsv
{
    /// <summary>Writes one row per `SpeakerId`; no `PersonId`, no identity.</summary>
    public static string Write(MeetingAnalytics analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);

        var builder = new StringBuilder();
        builder.AppendLine("SpeakerId,TalkMs,TalkShare,Turns,AvgTurnMs,LongestTurnMs,Words,WordsPerMinute,PersonId,DisplayName,Identity,UnknownId");
        Dictionary<int, MeetingSpeakerLabel> labels = analytics.SpeakerLabels.ToDictionary(l => l.SpeakerId);
        foreach (SpeakerTalkStats speaker in analytics.Speakers)
        {
            MeetingSpeakerLabel? label = labels.GetValueOrDefault(speaker.SpeakerId);
            builder.Append(speaker.SpeakerId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.TalkTime.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.TalkShare.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.TurnCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.AverageTurnDuration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.LongestTurn.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.WordCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.WordsPerMinute.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(label?.PersonId)).Append(',')
                .Append(Escape(label?.DisplayName)).Append(',')
                .Append(label?.Status.ToString() ?? MeetingSpeakerIdentityStatus.Anonymous.ToString()).Append(',')
                .Append(Escape(label?.UnknownId))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
