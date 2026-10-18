using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The model manager's own state: which rows are worth a diarization check, and
/// what a row shows while it is downloading.
/// </summary>
[Collection("UiGlobalState")]
public class ModelManagerStateTests
{
    private const string Moss = "moss-transcribe-diarize";
    private const string WhisperTiny = "whisper-tiny";

    /// <summary>
    /// Runs the body with a temporary cache root, then restores it. The override
    /// has to stay in place for the whole check, or the command reads the real
    /// cache and loads the real model.
    /// </summary>
    private static async Task InEmptyCacheAsync(Func<Task> body)
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-model-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            await body();
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    private static void Plant(string alias)
    {
        ModelDescriptor descriptor = ModelStore.Catalog.Single(m => m.Alias == alias);
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
    }

    /// <summary>
    /// The catalog is the reason the check can skip: it declares diarization for
    /// every model, and only a positive is worth confirming.
    /// </summary>
    [Fact]
    public void OnlyModelsUpstreamCallsPositiveAreWorthChecking()
    {
        var moss = new ModelCatalogItem(ModelStore.Catalog.Single(m => m.Alias == Moss));
        var whisper = new ModelCatalogItem(ModelStore.Catalog.Single(m => m.Alias == WhisperTiny));

        Assert.True(moss.UpstreamDeclaresDiarization);
        Assert.True(moss.DiarizationWorthChecking);

        Assert.False(whisper.UpstreamDeclaresDiarization);
        Assert.False(whisper.DiarizationWorthChecking);
    }

    /// <summary>
    /// A downloaded model the catalog already records as "no" is not loaded: the
    /// cell keeps upstream's answer and no weights are touched.
    /// </summary>
    [Fact]
    public async Task AModelTheCatalogSaysCannotDiarizeIsNotLoaded()
    {
        await InEmptyCacheAsync(async () =>
        {
            Plant(WhisperTiny);
            var viewModel = new ModelManagerViewModel();
            ModelCatalogItem item = viewModel.Models.Single(m => m.Alias == WhisperTiny);

            await viewModel.CheckDiarizationCommand.ExecuteAsync(null);

            // Nothing was asked of the model, so nothing changed on the row: a load
            // would have turned this into LoadFailed.
            Assert.Equal(ModelCatalogItem.DiarizationSupport.Unknown, item.Diarization);
            Assert.Contains("Nothing to check", viewModel.StatusMessage, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A downloaded model the catalog records as positive is loaded, so the
    /// unverified claim is checked on this machine. The planted file cannot load,
    /// which is itself the proof that the check reached the model.
    /// </summary>
    [Fact]
    public async Task AModelTheCatalogSaysDiarizesIsChecked()
    {
        await InEmptyCacheAsync(async () =>
        {
            Plant(Moss);
            var viewModel = new ModelManagerViewModel();
            ModelCatalogItem item = viewModel.Models.Single(m => m.Alias == Moss);

            await viewModel.CheckDiarizationCommand.ExecuteAsync(null);

            // It reached the model (LoadFailed, not Unknown) and the summary says so.
            Assert.Equal(ModelCatalogItem.DiarizationSupport.LoadFailed, item.Diarization);
            Assert.Contains("could not be loaded", viewModel.StatusMessage, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The row carries its own download state, so the button and the bar beside it
    /// can show the fetch without reading the view model's single busy alias.
    /// </summary>
    [Fact]
    public void TheRowShowsItsOwnDownloadState()
    {
        var item = new ModelCatalogItem(ModelStore.Catalog[0]);
        Assert.Equal("Download", item.DownloadButtonText);
        Assert.False(item.IsDownloading);

        item.SetDownloading(true);
        Assert.True(item.IsDownloading);
        Assert.Equal("Downloading…", item.DownloadButtonText);

        item.SetDownloadProgress(0.5);
        Assert.Equal(0.5, item.DownloadProgress);

        item.SetDownloading(false);
        Assert.False(item.IsDownloading);
        Assert.Equal(0, item.DownloadProgress);
        Assert.Equal("Download", item.DownloadButtonText);
    }
}
