namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Keeps anonymous speakers across transcripts, then identifies later.
/// </summary>
/// <remarks>
/// No raw audio by default. `SpeakerId` numeric ids remain per-run; `UnknownId` is a local cluster id.
/// `Matched` unknown cluster != `PersonId`. `Known` real-person matching remains `VoiceprintMatcher`.
/// No `nl`, no `fr`, no automatic raw transcript rewriting, no production privacy built-in.
/// </remarks>
public sealed class UnknownSpeakerTracker
{
    private readonly IUnknownSpeakerStore store;
    private readonly IVoiceprintStore? voiceprintStore;
    private readonly string embeddingModelId;
    private readonly AmbiguityPolicy clusterPolicy;

    /// <summary>Creates tracker over local unknown + optional known voiceprint stores.</summary>
    public UnknownSpeakerTracker(
        IUnknownSpeakerStore store,
        string embeddingModelId,
        AmbiguityPolicy clusterPolicy,
        IVoiceprintStore? voiceprintStore = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModelId);
        ArgumentNullException.ThrowIfNull(clusterPolicy);
        clusterPolicy.Validate();

        this.store = store;
        this.voiceprintStore = voiceprintStore;
        this.embeddingModelId = embeddingModelId;
        this.clusterPolicy = clusterPolicy;
    }

    /// <summary>
    /// Records an `Unknown` speaker snippet; clusters with previous `Pending` unknown if match.
    /// Caller must not call for `Known`; `Ambiguous` should normally re-check first.
    /// </summary>
    public async ValueTask<UnknownSpeaker> RecordUnknownAsync(
        string transcriptId,
        SpeakerSnippet snippet,
        float[] embedding,
        string language = "",
        string condition = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptId);
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(embedding.Length);
        ValidateFinite(embedding);

        IReadOnlyList<UnknownSpeaker> current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        UnknownSpeakerMatch match = UnknownSpeakerMatcher.MatchPending(embedding, embeddingModelId, current, clusterPolicy);

        UnknownSpeakerSource source = new(
            transcriptId,
            snippet.RunSpeakerId,
            snippet.Start,
            snippet.End,
            snippet.DiarizationProbability,
            SnippetSha256(snippet.Pcm16kMono),
            language,
            condition);

        UnknownSpeaker updated;

        if (match.Matched && match.UnknownId is { } unknownId)
        {
            UnknownSpeaker existing = current.First(u => string.Equals(u.UnknownId, unknownId, StringComparison.Ordinal));
            var embeddings = existing.Embeddings.Concat([embedding]).ToArray();
            var sources = existing.Sources.Concat([source]).ToArray();

            updated = existing with
            {
                Embeddings = embeddings,
                Sources = sources,
                LastSeen = DateTimeOffset.UtcNow,
                SourceSampleCount = embeddings.Length,
            };
        }
        else
        {
            string id = $"unknown-{Guid.NewGuid():N}";
            DateTimeOffset now = DateTimeOffset.UtcNow;
            updated = new UnknownSpeaker(
                id,
                embeddingModelId,
                embedding.Length,
                [embedding],
                [source],
                now,
                now,
                UnknownSpeakerStatus.Pending,
                PersonId: null,
                DisplayName: $"Unknown {id}",
                SourceSampleCount: 1);
        }

        await store.AddOrUpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Later identifies `UnknownId` as `PersonId`; optionally saves a `Voiceprint`.
    /// No raw transcript mutation; caller re-renders exports with `RetroactiveSpeakerRelabeler`.
    /// </summary>
    public async ValueTask<UnknownSpeakerIdentified> IdentifyLaterAsync(
        string unknownId,
        string personId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unknownId);
        ArgumentException.ThrowIfNullOrWhiteSpace(personId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        IReadOnlyList<UnknownSpeaker> current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        UnknownSpeaker existing = current.FirstOrDefault(u => string.Equals(u.UnknownId, unknownId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"UnknownSpeaker not found: {unknownId}");

        UnknownSpeaker identified = existing with
        {
            Status = UnknownSpeakerStatus.Identified,
            PersonId = personId,
            DisplayName = displayName,
            LastSeen = DateTimeOffset.UtcNow,
        };

        Voiceprint voiceprint = new(
            personId,
            displayName,
            identified.EmbeddingModelId,
            identified.Dimension,
            identified.Embeddings,
            DateTimeOffset.UtcNow,
            identified.SourceSampleCount);

        await store.AddOrUpdateAsync(identified, cancellationToken).ConfigureAwait(false);

        if (voiceprintStore is not null)
        {
            await voiceprintStore.AddOrUpdateAsync(voiceprint, cancellationToken).ConfigureAwait(false);
        }

        return new UnknownSpeakerIdentified(identified, voiceprint);
    }

    /// <summary>Rejects an anonymous cluster; no `PersonId` should be inferred.</summary>
    public async ValueTask<UnknownSpeaker> RejectAsync(string unknownId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unknownId);

        IReadOnlyList<UnknownSpeaker> current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        UnknownSpeaker existing = current.FirstOrDefault(u => string.Equals(u.UnknownId, unknownId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"UnknownSpeaker not found: {unknownId}");

        UnknownSpeaker rejected = existing with { Status = UnknownSpeakerStatus.Rejected, LastSeen = DateTimeOffset.UtcNow };
        await store.AddOrUpdateAsync(rejected, cancellationToken).ConfigureAwait(false);
        return rejected;
    }

    private static void ValidateFinite(float[] values)
    {
        foreach (float value in values)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("Embedding contains NaN or infinity.", nameof(values));
            }
        }
    }

    private static string SnippetSha256(float[] pcm)
    {
        byte[] bytes = new byte[pcm.Length * sizeof(float)];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
