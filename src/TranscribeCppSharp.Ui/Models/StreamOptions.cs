using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Ui.Models;

public class StreamOptions
{
    public StreamCommitPolicy CommitPolicy { get; set; } = StreamCommitPolicy.StreamCommitAuto;
    public BackendRequest? BackendRequest { get; set; }
    public BackendDevice? Device { get; set; }

    // Streaming extensions
    public uint? StablePrefixAgreement { get; set; }
    public int? MoonshineMinDecodeIntervalMs { get; set; }
    public int? ParakeetAttContextRight { get; set; }
    public int? ParakeetLeftMs { get; set; }
    public int? ParakeetChunkMs { get; set; }
    public int? ParakeetRightMs { get; set; }
    public SortformerPreset? SortformerPreset { get; set; }
    public int? VoxtralNumDelayTokens { get; set; }
    public int? VoxtralMinDecodeIntervalMs { get; set; }
}

public class StreamUpdate
{
    public string FullText { get; set; } = string.Empty;
    public string CommittedText { get; set; } = string.Empty;
    public string TentativeText { get; set; } = string.Empty;
    public bool IsFinal { get; set; }
}
