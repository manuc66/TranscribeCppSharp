namespace TranscribeCppSharp.Voiceprint;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Persistence for local voiceprints. No cloud, no audio, no encryption inherently.</summary>
public interface IVoiceprintStore
{
    /// <summary>Loads saved voiceprints.</summary>
    ValueTask<IReadOnlyList<Voiceprint>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces a voiceprint by <see cref="Voiceprint.PersonId"/>.</summary>
    ValueTask<Voiceprint> AddOrUpdateAsync(Voiceprint voiceprint, CancellationToken cancellationToken = default);

    /// <summary>Removes a voiceprint by person id; returns whether anything was removed.</summary>
    ValueTask<bool> RemoveAsync(string personId, CancellationToken cancellationToken = default);
}
