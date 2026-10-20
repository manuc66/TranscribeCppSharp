using System.Net;
using System.Net.Http;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Models tab's **Cancel download** button. The store's own cancellation is
/// covered by <c>ModelStoreDownloadTests</c>; what is checked here is the wiring
/// above it: the view model has a token to cancel, the row returns to idle, and
/// the status line says cancelled rather than failed.
/// </summary>
[Collection("UiGlobalState")]
public class ModelDownloadCancelTests : IDisposable
{
    private const string WhisperTiny = "whisper-tiny";

    private readonly Func<HttpClient> _originalFactory = ModelStore.HttpClientFactory;
    private readonly string? _originalCacheRoot = ModelStore.CacheRootOverride;

    public ModelDownloadCancelTests()
    {
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-cancel-" + Guid.NewGuid().ToString("N"));

        // A request that never answers: the only way out of the download is the
        // token, which is exactly what the button is supposed to supply.
        ModelStore.HttpClientFactory = () =>
            new HttpClient(new NeverAnswersHandler()) { Timeout = TimeSpan.FromMinutes(2) };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ModelStore.HttpClientFactory = _originalFactory;
        ModelStore.CacheRootOverride = _originalCacheRoot;
    }

    [Fact]
    public async Task CancellingADownloadReturnsTheRowToIdle()
    {
        var viewModel = new ModelManagerViewModel();
        var item = viewModel.VisibleModels.First(m => m.Alias == WhisperTiny);
        Assert.False(item.IsCached);

        viewModel.DownloadCommand.Execute(item);
        Assert.True(viewModel.IsDownloading);
        Assert.True(viewModel.IsBusy);
        Assert.True(item.IsDownloading);

        viewModel.CancelDownloadCommand.Execute(null);
        await viewModel.DownloadCommand.ExecutionTask!;

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsDownloading);
        Assert.False(item.IsDownloading);
        Assert.Contains("cancelled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        // A cancelled download caches nothing, so the row is downloadable again.
        Assert.False(item.IsCached);
    }

    /// <summary>
    /// A handler whose request only ends when it is cancelled.
    /// </summary>
    private sealed class NeverAnswersHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The infinite delay should have thrown.");
        }
    }
}
