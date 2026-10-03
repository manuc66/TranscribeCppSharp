using System.Text.Json;

namespace TranscribeCppSharp.Ui.Services;

public class ModelDownloadService : IModelDownloadService
{
    private readonly string _modelsJsonPath;
    private readonly string _cacheDirectory;

    public ModelDownloadService()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TranscribeCppSharp", "models");
        Directory.CreateDirectory(_cacheDirectory);
        _modelsJsonPath = Path.Combine(AppContext.BaseDirectory, "models.json");
    }

    public IReadOnlyList<ModelInfo> GetAvailableModels()
    {
        if (!File.Exists(_modelsJsonPath))
            return Array.Empty<ModelInfo>();

        var json = File.ReadAllText(_modelsJsonPath);
        var models = JsonSerializer.Deserialize<List<ModelInfo>>(json);
        return models ?? Array.Empty<ModelInfo>();
    }

    public async Task DownloadModelAsync(
        string modelAlias,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modelInfo = await GetModelInfoAsync(modelAlias);
        if (modelInfo == null)
            throw new InvalidOperationException($"Model '{modelAlias}' not found");

        var targetPath = Path.Combine(_cacheDirectory, modelInfo.Filename);
        if (File.Exists(targetPath))
        {
            progress?.Report(1.0);
            return;
        }

        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(modelInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;

            if (totalBytes > 0)
                progress?.Report((double)totalRead / totalBytes);
        }

        progress?.Report(1.0);
    }

    public Task<ModelInfo?> GetModelInfoAsync(string modelAlias)
    {
        var models = GetAvailableModels();
        return Task.FromResult(models.FirstOrDefault(m => m.Alias == modelAlias));
    }
}
