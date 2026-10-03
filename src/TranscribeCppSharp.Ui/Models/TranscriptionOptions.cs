using TranscribeCppSharp;

namespace TranscribeCppSharp.Ui.Models;

public class TranscriptionOptions
{
    public string Language { get; set; } = "en";
    public TimestampKind TimestampKind { get; set; } = TimestampKind.Segment;
    public DiarizeMode DiarizeMode { get; set; } = DiarizeMode.Auto;
    public PncMode? PncMode { get; set; }
    public ItnMode? ItnMode { get; set; }
    public BackendRequest? BackendRequest { get; set; }
    public BackendDevice? Device { get; set; }
}
