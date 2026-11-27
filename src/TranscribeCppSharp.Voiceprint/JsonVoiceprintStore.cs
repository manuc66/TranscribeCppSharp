namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Local JSON store for voiceprints. No cloud, no audio storage.</summary>
/// <remarks>Not safe for concurrent writers. Personal/sensitive data: protect storage.</remarks>
public sealed class JsonVoiceprintStore : IVoiceprintStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string path;

    /// <summary>Creates a store at a local JSON file path.</summary>
    public JsonVoiceprintStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    /// <summary>Loads saved voiceprints; missing file means empty.</summary>
    public async ValueTask<IReadOnlyList<Voiceprint>> LoadAsync(CancellationToken cancellationToken = default)
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

        Voiceprint[]? loaded = JsonSerializer.Deserialize<Voiceprint[]>(json, JsonOptions);
        return loaded ?? [];
    }

    /// <summary>Adds or replaces a voiceprint by <see cref="Voiceprint.PersonId"/>, saving locally.</summary>
    public async ValueTask<Voiceprint> AddOrUpdateAsync(Voiceprint voiceprint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(voiceprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceprint.PersonId);
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceprint.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceprint.EmbeddingModelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(voiceprint.Dimension);
        ArgumentNullException.ThrowIfNull(voiceprint.Embeddings);

        IReadOnlyList<Voiceprint> current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        Voiceprint[] updated = current
            .Where(v => !string.Equals(v.PersonId, voiceprint.PersonId, StringComparison.Ordinal))
            .Append(voiceprint)
            .ToArray();

        await SaveAllAsync(updated, cancellationToken).ConfigureAwait(false);
        return voiceprint;
    }

    /// <summary>Removes a voiceprint by person id; returns whether anything was removed.</summary>
    public async ValueTask<bool> RemoveAsync(string personId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personId);

        IReadOnlyList<Voiceprint> current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        Voiceprint[] kept = current
            .Where(v => !string.Equals(v.PersonId, personId, StringComparison.Ordinal))
            .ToArray();

        if (kept.Length == current.Count)
        {
            return false;
        }

        await SaveAllAsync(kept, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask SaveAllAsync(IReadOnlyList<Voiceprint> voiceprints, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(voiceprints, JsonOptions);
        string tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(tempPath, path, overwrite: true);
    }
}
