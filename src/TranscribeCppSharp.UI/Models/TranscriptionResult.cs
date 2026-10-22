namespace TranscribeCppSharp.UI.Models;

public class TranscriptionResult
{
    /// <summary>The roles the model served; the run used the first one it has.</summary>
    public TranscribeRole Roles { get; set; }

    public string FullText { get; set; } = string.Empty;
    public string DetectedLanguage { get; set; } = string.Empty;
    public bool WasAborted { get; set; }
    public bool WasTruncated { get; set; }

    /// <summary>
    /// The audio file's length, when the service read it. Null for a result from an
    /// older history entry that did not record it.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Segments timed against the whole file, with the seam between two
    /// windows deduplicated. <see cref="TranscribeCppSharp.Audio.MergedSegment"/>
    /// rather than the native <see cref="TranscribeCppSharp.SegmentResult"/>
    /// because these have absolute times and may come from several windows.
    /// </summary>
    public List<Audio.MergedSegment> Segments { get; set; } = new();

    public List<WordResult> Words { get; set; } = new();
    public List<SpeakerSegmentResult> SpeakerSegments { get; set; } = new();

    /// <summary>Ranked languages, for a model that serves the LANGID role.</summary>
    public List<LangIdSession.LangIdCandidate> LangIdCandidates { get; set; } = new();

    /// <summary>Speech segments, for a model that serves the VAD role.</summary>
    public List<VadSession.VadSegmentResult> VadSegments { get; set; } = new();

    /// <summary>
    /// The window the ASR run actually used, after any automatic lowering. Null
    /// for a role that does not use the window setting.
    /// </summary>
    /// <remarks>
    /// The caller may have asked for a larger window and the service lowered it
    /// after an out-of-memory failure. This is the value the transcript was
    /// produced with, so it is what the history and the field must show.
    /// </remarks>
    public int? EffectiveWindowSeconds { get; set; }

    /// <summary>
    /// The window the caller asked for, when the service lowered it. Null when no
    /// adjustment happened.
    /// </summary>
    public int? RequestedWindowSeconds { get; set; }

    /// <summary>
    /// The lowest free memory observed on the model's device during the run, when
    /// it could be read. Null when the device reports no free figure.
    /// </summary>
    public MemoryObservation? Memory { get; set; }
}
