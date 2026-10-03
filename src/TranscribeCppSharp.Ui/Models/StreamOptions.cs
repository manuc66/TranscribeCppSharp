using TranscribeCppSharp;

namespace TranscribeCppSharp.Ui.Models;

public class StreamOptions
{
    public StreamCommitPolicy CommitPolicy { get; set; } = StreamCommitPolicy.Auto;
    public BackendRequest? BackendRequest { get; set; }
    public BackendDevice? Device { get; set; }
}

public class StreamUpdate
{
    public string FullText { get; set; } = string.Empty;
    public string CommittedText { get; set; } = string.Empty;
    public string TentativeText { get; set; } = string.Empty;
    public bool IsFinal { get; set; }
}
