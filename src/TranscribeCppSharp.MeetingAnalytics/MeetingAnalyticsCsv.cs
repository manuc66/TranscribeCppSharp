namespace TranscribeCppSharp.MeetingAnalytics;

using System;
using System.Globalization;
using System.Text;

/// <summary>CSV exporter for per-speaker anonymous statistics.</summary>
public static class MeetingAnalyticsCsv
{
    /// <summary>Writes one row per `SpeakerId`; no `PersonId`, no identity.</summary>
    public static string Write(MeetingAnalytics analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);

        var builder = new StringBuilder();
        builder.AppendLine("SpeakerId,TalkMs,TalkShare,Turns,AvgTurnMs,LongestTurnMs,Words,WordsPerMinute");
        foreach (SpeakerTalkStats speaker in analytics.Speakers)
        {
            builder.Append(speaker.SpeakerId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.TalkTime.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.TalkShare.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.TurnCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.AverageTurnDuration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.LongestTurn.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.WordCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(speaker.WordsPerMinute.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return builder.ToString();
    }
}
