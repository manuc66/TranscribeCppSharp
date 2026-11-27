namespace TranscribeCppSharp.Voiceprint;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Local persistence for anonymous pending/identified speakers. No audio, no encryption inherently.</summary>
public interface IUnknownSpeakerStore
{
    /// <summary>Loads unknown speakers.</summary>
    ValueTask<IReadOnlyList<UnknownSpeaker>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces by `UnknownId`.</summary>
    ValueTask<UnknownSpeaker> AddOrUpdateAsync(UnknownSpeaker speaker, CancellationToken cancellationToken = default);

    /// <summary>Removes by `UnknownId`; returns whether anything was removed.</summary>
    ValueTask<bool> RemoveAsync(string unknownId, CancellationToken cancellationToken = default);
}
