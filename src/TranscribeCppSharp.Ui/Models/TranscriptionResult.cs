namespace TranscribeCppSharp.Ui.Models;

public class TranscriptionResult
{
    public string FullText { get; set; } = string.Empty;
    public string DetectedLanguage { get; set; } = string.Empty;
    public bool WasAborted { get; set; }
    public bool WasTruncated { get; set; }

    /// <summary>
    /// Segments timed against the whole file, with the seam between two
    /// windows deduplicated. <see cref="TranscribeCppSharp.Audio.MergedSegment"/>
    /// rather than the native <see cref="TranscribeCppSharp.SegmentResult"/>
    /// because these have absolute times and may come from several windows.
    /// </summary>
    public List<Audio.MergedSegment> Segments { get; set; } = new();

    public List<WordResult> Words { get; set; } = new();
    public List<SpeakerSegmentResult> SpeakerSegments { get; set; } = new();
}
