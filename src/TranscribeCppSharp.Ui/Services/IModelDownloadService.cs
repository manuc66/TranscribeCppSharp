using TranscribeCppSharp.Ui.Models;

namespace TranscribeCppSharp.Ui.Services;

public interface IModelDownloadService
{
    IReadOnlyList<ModelInfo> GetAvailableModels();
    Task DownloadModelAsync(
        string modelAlias,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
    Task<ModelInfo?> GetModelInfoAsync(string modelAlias);
}
