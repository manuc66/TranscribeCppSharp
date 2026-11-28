namespace TranscribeCppSharp.MeetingAnalytics;

using System;

/// <summary>Options for per-recording speaker analytics. No identity, no `nl`, no biometrics.</summary>
public sealed record MeetingAnalyticsOptions(
    TimeSpan? RecordingDuration = null,
    TimeSpan MinSilence = default,
    TimeSpan MinInterruptedSpeech = default,
    TimeSpan BackchannelMaxDuration = default,
    TimeSpan MinOverlap = default)
{
    /// <summary>Creates options with practical provisional defaults; not calibrated production thresholds.</summary>
    public static MeetingAnalyticsOptions Default => new(
        MinSilence: TimeSpan.FromSeconds(1),
        MinInterruptedSpeech: TimeSpan.FromMilliseconds(500),
        BackchannelMaxDuration: TimeSpan.FromMilliseconds(1500),
        MinOverlap: TimeSpan.Zero);

    /// <summary>Validates structural options.</summary>
    public void Validate()
    {
        if (RecordingDuration is { } duration && duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RecordingDuration));
        }

        ThrowIfNegative(MinSilence, nameof(MinSilence));
        ThrowIfNegative(MinInterruptedSpeech, nameof(MinInterruptedSpeech));
        ThrowIfNegative(BackchannelMaxDuration, nameof(BackchannelMaxDuration));
        ThrowIfNegative(MinOverlap, nameof(MinOverlap));
    }

    private static void ThrowIfNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
