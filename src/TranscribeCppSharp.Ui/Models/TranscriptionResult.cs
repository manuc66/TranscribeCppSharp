namespace TranscribeCppSharp.Ui.Models;

public class TranscriptionResult
{
    public string FullText { get; set; } = string.Empty;
    public string DetectedLanguage { get; set; } = string.Empty;
    public bool WasAborted { get; set; }
    public bool WasTruncated { get; set; }
    public List<TranscribeCppSharp.SegmentResult> Segments { get; set; } = new();
    public List<TranscribeCppSharp.WordResult> Words { get; set; } = new();
    public List<TranscribeCppSharp.SpeakerSegmentResult> SpeakerSegments { get; set; } = new();
}
