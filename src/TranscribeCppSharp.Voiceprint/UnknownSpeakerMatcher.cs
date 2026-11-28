namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Matches new embeddings against previously saved `Pending` unknown speakers. Not `PersonId` verification.</summary>
public static class UnknownSpeakerMatcher
{
    /// <summary>
    /// Uses `VoiceprintMatcher` on `UnknownSpeaker.ToPendingVoiceprint`; only `Pending` clusters.
    /// `Matched` means **same anonymous cluster**, not `Known`, not `PersonId`, not `nl`.
    /// </summary>
    public static UnknownSpeakerMatch MatchPending(
        float[] embedding,
        string embeddingModelId,
        IReadOnlyList<UnknownSpeaker> unknowns,
        AmbiguityPolicy policy)
        => MatchPendingWithRechecks(embedding, embeddingModelId, unknowns, policy, recheckEmbeddings: []);

    /// <summary>
    /// `MatchPending` + bounded alternative embeddings. `Matched` anonymous cluster != `Known`, `PersonId`.
    /// Caller supplies rechecks; no re-diarization, no `nl`, no `PersonCorrect`.
    /// </summary>
    public static UnknownSpeakerMatch MatchPendingWithRechecks(
        float[] embedding,
        string embeddingModelId,
        IReadOnlyList<UnknownSpeaker> unknowns,
        AmbiguityPolicy policy,
        IReadOnlyList<float[]> recheckEmbeddings)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModelId);
        ArgumentNullException.ThrowIfNull(unknowns);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(recheckEmbeddings);
        policy.Validate();

        List<Voiceprint> pending = unknowns
            .Where(u => u is not null
                && u.Status == UnknownSpeakerStatus.Pending
                && !string.IsNullOrWhiteSpace(u.SchemaVersion)
                && string.Equals(u.SchemaVersion, VoiceprintMatcher.SupportedSchemaVersion, StringComparison.Ordinal)
                && string.Equals(u.EmbeddingModelId, embeddingModelId, StringComparison.Ordinal)
                && u.Dimension == embedding.Length
                && u.Embeddings is { Count: > 0 })
            .Select(u => u.ToPendingVoiceprint())
            .ToList();

        // Reject NaN/Inf even when there is no pending cluster; do not hide invalid input as Unknown.
        ValidateFinite(embedding);

        if (pending.Count == 0 || embedding.Length == 0)
        {
            return UnknownSpeakerMatch.Unknown();
        }

        SpeakerMatch match = VoiceprintMatcher.MatchWithRechecks(embedding, embeddingModelId, pending, policy, recheckEmbeddings);

        // Unknown anonymous cluster is not `SpeakerIdentityDecision.Known` real-person identity.
        bool matched = match.Decision == SpeakerIdentityDecision.Known && match.PersonId is not null;

        return new UnknownSpeakerMatch(
            matched,
            match.PersonId,
            match.BestScore,
            match.SecondBestScore,
            match.Margin,
            match.ReverifiedCount);
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
}
