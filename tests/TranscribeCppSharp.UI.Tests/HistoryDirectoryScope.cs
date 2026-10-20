using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Points <see cref="TranscriptionHistory"/> at a temporary directory for the
/// duration of a test, and deletes it afterwards.
/// </summary>
internal sealed class HistoryDirectoryScope : IDisposable
{
    private readonly string? _previous = TranscriptionHistory.DirectoryOverride;

    public HistoryDirectoryScope()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(), "tcsharp-history-" + Guid.NewGuid().ToString("N"));
        TranscriptionHistory.DirectoryOverride = DirectoryPath;
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
        TranscriptionHistory.DirectoryOverride = _previous;
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // A test that leaves a file open should not fail on cleanup.
        }
    }
}
