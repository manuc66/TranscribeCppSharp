namespace TranscribeCppSharp.Ui.Models;

public class BatchResult
{
    public List<BatchItemResult> Items { get; set; } = new();
}

public class BatchItemResult
{
    public string AudioPath { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public string DetectedLanguage { get; set; } = string.Empty;
    public string Status { get; set; } = "Ok";
}
