namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Local JSON `IUnknownSpeakerStore`. No audio, no cloud, no encryption, no concurrent writers.</summary>
public sealed class JsonUnknownSpeakerStore : IUnknownSpeakerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly string path;

    /// <summary>Creates a local JSON store.</summary>
    public JsonUnknownSpeakerStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UnknownSpeaker>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path))
        {
            return [];
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        UnknownSpeaker[]? loaded = JsonSerializer.Deserialize<UnknownSpeaker[]>(json, JsonOptions);
        return loaded ?? [];
    }

    /// <inheritdoc />
    public async ValueTask<UnknownSpeaker> AddOrUpdateAsync(UnknownSpeaker speaker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentException.ThrowIfNullOrWhiteSpace(speaker.UnknownId);
        ArgumentException.ThrowIfNullOrWhiteSpace(speaker.EmbeddingModelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(speaker.Dimension);
        ArgumentNullException.ThrowIfNull(speaker.Embeddings);
        ArgumentNullException.ThrowIfNull(speaker.Sources);

        IReadOnlyList<UnknownSpeaker> current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        UnknownSpeaker[] updated = current
            .Where(s => !string.Equals(s.UnknownId, speaker.UnknownId, StringComparison.Ordinal))
            .Append(speaker)
            .ToArray();

        await SaveAllAsync(updated, cancellationToken).ConfigureAwait(false);
        return speaker;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RemoveAsync(string unknownId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unknownId);

        IReadOnlyList<UnknownSpeaker> current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        UnknownSpeaker[] kept = current
            .Where(s => !string.Equals(s.UnknownId, unknownId, StringComparison.Ordinal))
            .ToArray();

        if (kept.Length == current.Count)
        {
            return false;
        }

        await SaveAllAsync(kept, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask SaveAllAsync(IReadOnlyList<UnknownSpeaker> speakers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(speakers, JsonOptions);
        string tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(tempPath, path, overwrite: true);
    }
}
