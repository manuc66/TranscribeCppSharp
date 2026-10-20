using System.Collections.ObjectModel;
using System.Text.Json;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Writing batch results to disk. The row content is not the subject here: the
/// point is that a failed write is reported to the status line instead of
/// escaping the command and taking the window down.
/// </summary>
[Collection("UiGlobalState")]
public class BatchExportTests
{
    private static void WithViewModel(Action<BatchViewModel> test)
    {
        string? previousCache = ModelStore.CacheRootOverride;
        string? previousLog = UiLog.FilePathOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-batch-export-" + Guid.NewGuid().ToString("N"));
        UiLog.FilePathOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-batch-log-" + Guid.NewGuid().ToString("N"), "ui.log");
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel())
            {
                Results = new ObservableCollection<BatchItemResult>
                {
                    new() { AudioPath = "/audio/one.wav", FullText = "first", Status = "Ok" },
                    new() { AudioPath = "/audio/two.wav", FullText = "second", Status = "Ok" },
                },
            };
            test(viewModel);
        }
        finally
        {
            ModelStore.CacheRootOverride = previousCache;
            UiLog.FilePathOverride = previousLog;
        }
    }

    private static string TempPath(string extension)
        => Path.Combine(
            Path.GetTempPath(),
            "tcsharp-batch-export-" + Guid.NewGuid().ToString("N") + extension);

    [Fact]
    public void TheJsonExportIsWrittenAndReported()
    {
        WithViewModel(viewModel =>
        {
            string path = TempPath(".json");
            try
            {
                viewModel.ExportResultsToAsync(path).GetAwaiter().GetResult();

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                Assert.Equal(2, document.RootElement.GetArrayLength());
                Assert.Contains(path, viewModel.StatusMessage, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void TheTextExportNamesEachFile()
    {
        WithViewModel(viewModel =>
        {
            string path = TempPath(".txt");
            try
            {
                viewModel.ExportResultsToAsync(path).GetAwaiter().GetResult();

                string text = File.ReadAllText(path);
                Assert.Contains("one.wav:", text, StringComparison.Ordinal);
                Assert.Contains("second", text, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void AWriteThatFailsIsReportedInsteadOfThrown()
    {
        WithViewModel(viewModel =>
        {
            string directory = Path.Combine(
                Path.GetTempPath(), "tcsharp-batch-export-dir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                viewModel.ExportResultsToAsync(directory).GetAwaiter().GetResult();

                Assert.Contains("Could not export", viewModel.StatusMessage, StringComparison.Ordinal);
                Assert.True(File.Exists(UiLog.FilePath));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }
}
