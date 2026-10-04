using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Ui.Models;

public class TranscriptionOptions
{
    public string Language { get; set; } = "en";
    public TimestampKind TimestampKind { get; set; } = TimestampKind.TimestampsSegment;
    public DiarizeMode DiarizeMode { get; set; } = DiarizeMode.DiarizeModeDefault;
    public PncMode? PncMode { get; set; }
    public ItnMode? ItnMode { get; set; }
    public BackendRequest? BackendRequest { get; set; }
    public BackendDevice? Device { get; set; }

    /// <summary>
    /// Maximum per-transcription window in seconds. Audio longer than this
    /// is split into overlapping windows (1s overlap) and results are merged.
    /// </summary>
    public int WindowSeconds { get; set; } = 300;

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
