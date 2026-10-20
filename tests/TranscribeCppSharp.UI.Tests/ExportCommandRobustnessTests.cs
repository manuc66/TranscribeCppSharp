using System.Collections.ObjectModel;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Exporting a finished run. The document generation is covered by the export
/// option tests; this class covers the disk side: a successful write, and a write
/// that fails without throwing out of the command.
/// </summary>
[Collection("UiGlobalState")]
public class ExportCommandRobustnessTests
{
    private static void WithViewModel(Action<TranscriptionViewModel> test)
    {
        string? previousCache = ModelStore.CacheRootOverride;
        string? previousLog = UiLog.FilePathOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-export-disk-" + Guid.NewGuid().ToString("N"));
        UiLog.FilePathOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-export-log-" + Guid.NewGuid().ToString("N"), "ui.log");
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.Result = new TranscriptionResult
            {
                FullText = "hello from the export",
                Segments =
                [
                    new MergedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello from the export", 0),
                ],
            };
            viewModel.FullText = viewModel.Result.FullText;
            viewModel.Segments = new ObservableCollection<MergedSegment>(viewModel.Result.Segments);
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
            "tcsharp-export-" + Guid.NewGuid().ToString("N") + extension);

    [Fact]
    public void TheTextExportIsWrittenAndReported()
    {
        WithViewModel(viewModel =>
        {
            string path = TempPath(".txt");
            try
            {
                viewModel.ExportToAsync(path).GetAwaiter().GetResult();

                Assert.Equal("hello from the export", File.ReadAllText(path));
                Assert.Contains(path, viewModel.StatusMessage, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void TheExportFormatFollowsTheExtension()
    {
        WithViewModel(viewModel =>
        {
            string path = TempPath(".md");
            try
            {
                viewModel.ExportToAsync(path).GetAwaiter().GetResult();

                string markdown = File.ReadAllText(path);
                Assert.StartsWith("# ", markdown, StringComparison.Ordinal);
                Assert.Contains("hello from the export", markdown, StringComparison.Ordinal);
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
            // A directory where a file is expected: the write cannot succeed on
            // any supported platform.
            string directory = Path.Combine(
                Path.GetTempPath(), "tcsharp-export-dir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                viewModel.ExportToAsync(directory).GetAwaiter().GetResult();

                Assert.Contains("Could not export", viewModel.StatusMessage, StringComparison.Ordinal);
                Assert.Contains("ui.log", viewModel.StatusMessage, StringComparison.Ordinal);
                Assert.True(File.Exists(UiLog.FilePath));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void AMissingParentDirectoryIsReported()
    {
        WithViewModel(viewModel =>
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "tcsharp-export-missing-" + Guid.NewGuid().ToString("N"),
                "out.txt");

            viewModel.ExportToAsync(path).GetAwaiter().GetResult();

            Assert.Contains("Could not export", viewModel.StatusMessage, StringComparison.Ordinal);
            Assert.False(File.Exists(path));
        });
    }
}
