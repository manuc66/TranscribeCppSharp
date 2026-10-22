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
    /// <remarks>
    /// 30 s is a conservative default, not a model limit: the compute buffer
    /// grows with the window and the native API does not expose its size. In the
    /// reported Parakeet failure the whole file went into one call (the Window
    /// setting was ignored by an earlier milliseconds/seconds mix-up) and asked
    /// for 10.1 GB on Vulkan and 113.6 GB on the CPU; 30 s windows completed on
    /// both. Raise this only after checking the real allocation on the intended
    /// device.
    /// </remarks>
    public int WindowSeconds { get; set; } = 30;

    /// <summary>
    /// Whether the service may lower <see cref="WindowSeconds"/> and retry when
    /// the first window does not fit in memory.
    /// </summary>
    /// <remarks>
    /// Only lowering is automatic. Raising would need a measurement that the
    /// larger window fits, and the native API does not provide one; the caller
    /// raises the field itself and the run lowers it again if it has to.
    /// </remarks>
    public bool AutoAdjustWindow { get; set; } = true;

    /// <summary>
    /// Language codes a language-identification model may answer with
    /// (comma-separated in the UI, split here). Null/empty ranks every label.
    /// </summary>
    public string[]? AllowedLanguages { get; set; }

    /// <summary>
    /// Custom terms for a model that supports a vocabulary, in priority order.
    /// Null/empty passes no list. A model without the feature ignores it with a
    /// native warning, which the wrapper does not turn into an error.
    /// </summary>
    public string[]? Vocabulary { get; set; }

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
