namespace TranscribeCppSharp.Ui.Models;

public class TranscriptionResult
{
    public string FullText { get; set; } = string.Empty;
    public string DetectedLanguage { get; set; } = string.Empty;
    public bool WasAborted { get; set; }
    public bool WasTruncated { get; set; }
    public List<SegmentResult> Segments { get; set; } = new();
    public List<WordResult> Words { get; set; } = new();
    public List<SpeakerSegmentResult> SpeakerSegments { get; set; } = new();
}

public class SegmentResult
{
    public float Start { get; set; }
    public float End { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? SpeakerId { get; set; }
}

public class WordResult
{
    public float Start { get; set; }
    public float End { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class SpeakerSegmentResult
{
    public float Start { get; set; }
    public float End { get; set; }
    public string SpeakerId { get; set; } = string.Empty;
    public float Probability { get; set; }
}
