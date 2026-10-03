namespace TranscribeCppSharp.Ui.Models;

public class ModelInfo
{
    public string Alias { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Revision { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
}
