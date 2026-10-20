namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// The Transcription tab's export options, as they were last left. A record of
/// what the reader chose, nothing about the transcript.
/// </summary>
public readonly record struct ExportSettings(
    bool MarkPauses,
    int PauseThresholdSeconds,
    int TimestampIntervalSeconds);
