using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.UI.Models;

public class StreamOptions
{
    public StreamCommitPolicy CommitPolicy { get; set; } = StreamCommitPolicy.StreamCommitAuto;
    public BackendRequest? BackendRequest { get; set; }
    public BackendDevice? Device { get; set; }

    /// <summary>
    /// Seconds of audio per utterance before the stream is closed and a new one
    /// started, to bound the native working set. 0 keeps one utterance for the
    /// whole session. The reason and the cost are documented on
    /// <c>UtteranceRotation</c> (internal) and in docs/streaming-bench.md.
    /// </summary>
    public int UtteranceSeconds { get; set; } = 30;

    // Streaming extensions
    public uint? StablePrefixAgreement { get; set; }
    public int? MoonshineMinDecodeIntervalMs { get; set; }
    public int? ParakeetAttContextRight { get; set; }
    public int? ParakeetLeftMs { get; set; }
    public int? ParakeetChunkMs { get; set; }
    public int? ParakeetRightMs { get; set; }
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
