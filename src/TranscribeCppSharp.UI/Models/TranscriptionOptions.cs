using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.UI.Models;

public class TranscriptionOptions
{
    public string Language { get; set; } = "en";
    public TimestampKind TimestampKind { get; set; } = TimestampKind.TimestampsSegment;
    public DiarizeMode DiarizeMode { get; set; } = DiarizeMode.DiarizeModeDefault;

    /// <summary>
    /// Alias of a separate diarizer to combine with the run, or null for none.
    /// </summary>
    /// <remarks>
    /// For a transcriber that cannot attribute speakers itself: the model is run
    /// over the same audio after the transcription, and its speaker turns are
    /// joined to the segments by <see cref="Services.SpeakerAssignment"/>. The two
    /// are separate native runs — transcribe.cpp has no combined entry point.
    /// </remarks>
    public string? DiarizerModel { get; set; }

    public PncMode? PncMode { get; set; }
    public ItnMode? ItnMode { get; set; }
    public BackendRequest? BackendRequest { get; set; }
    public BackendDevice? Device { get; set; }

    /// <summary>
    /// Maximum per-transcription window in seconds. Audio longer than this
    /// is split into overlapping windows (1s overlap) and results are merged.
    /// </summary>
    public int WindowSeconds { get; set; } = 300;

    /// <summary>
    /// Language codes a language-identification model may answer with
    /// (comma-separated in the UI, split here). Null/empty ranks every label.
    /// </summary>
    public string[]? AllowedLanguages { get; set; }

    // Session parameters
    public int? Threads { get; set; }
    public KvType? KvType { get; set; }
    public int? ContextSize { get; set; }

    // Translation
    public TranscriptionTask? Task { get; set; }
    public string? TargetLanguage { get; set; }

    // Speculative decoding
    public int? SpecKDrafts { get; set; }

    // Keep special tags
    public bool? KeepSpecialTags { get; set; }

    // Whisper extensions
    public string? WhisperInitialPrompt { get; set; }
    public float? WhisperTemperature { get; set; }
    public float? WhisperTemperatureInc { get; set; }
    public float? WhisperCompressionRatioThold { get; set; }
    public float? WhisperLogprobThold { get; set; }
    public float? WhisperNoSpeechThold { get; set; }
    public uint? WhisperSeed { get; set; }
    public float? WhisperMaxInitialTimestamp { get; set; }
    public bool WhisperConditionOnPrevTokens { get; set; }
    public int? WhisperMaxPrevContextTokens { get; set; }
}
