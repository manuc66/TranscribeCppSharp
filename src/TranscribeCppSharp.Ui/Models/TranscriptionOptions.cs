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
}
