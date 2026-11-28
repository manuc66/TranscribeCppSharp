namespace TranscribeCppSharp.MeetingAnalytics;

using System;
using System.Text.Json;

/// <summary>JSON exporter for anonymous meeting analytics.</summary>
public static class MeetingAnalyticsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>Serializes analytics. No audio, no voiceprint, no `PersonId`.</summary>
    public static string Write(MeetingAnalytics analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);
        return JsonSerializer.Serialize(analytics, Options);
    }
}
