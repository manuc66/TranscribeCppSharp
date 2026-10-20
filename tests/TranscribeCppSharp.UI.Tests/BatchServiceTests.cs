using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The batch service against the test assets: a batch is a list of independent
/// files, so one unreadable file must not discard the items that finished or
/// stop the files after it.
/// </summary>
public class BatchServiceTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string Model => Path.Combine(RepoRoot, "test-models", "ggml-tiny.bin");

    private static string Audio => Path.Combine(RepoRoot, "test-audio", "jfk.wav");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TranscribeCppSharp.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static TranscriptionOptions Cpu()
        => new()
        {
            BackendRequest = TranscribeCppSharp.Interop.BackendRequest.BackendCpu,
            Language = "en",
        };

    [Fact]
    public async Task AnUnreadableFileDoesNotStopTheRestOfTheBatch()
    {
        if (!File.Exists(Model) || !File.Exists(Audio))
        {
            Assert.Skip($"test assets missing ({Model} or {Audio})");
        }

        string missing = Path.Combine(
            Path.GetTempPath(), "tcsharp-batch-missing-" + Guid.NewGuid().ToString("N") + ".wav");
        var reported = new List<int>();
        var progress = new CollectingProgress(reported.Add);

        List<BatchItemResult> results = await new TranscriptionService().BatchTranscribeAsync(
            [missing, Audio],
            Model,
            Cpu(),
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Equal(missing, results[0].AudioPath);
        Assert.StartsWith("Error:", results[0].Status, StringComparison.Ordinal);
        Assert.Empty(results[0].FullText);

        Assert.Equal(Audio, results[1].AudioPath);
        Assert.Equal("Ok", results[1].Status);
        Assert.NotEmpty(results[1].FullText);

        // A failed file is still a finished item: the progress reaches the end.
        Assert.Equal([1, 2], reported);
    }

    /// <summary>
    /// Progress&lt;T&gt; posts to the captured context, so its callbacks are not
    /// guaranteed to have run when the batch returns. This one is synchronous.
    /// </summary>
    private sealed class CollectingProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
