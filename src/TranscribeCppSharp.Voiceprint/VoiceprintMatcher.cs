namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Cosine matching against local voiceprints. No identity claim beyond score + margin.</summary>
public static class VoiceprintMatcher
{
    /// <summary>Matches one embedding. Caller must re-verify ambiguity.</summary>
    public static SpeakerMatch Match(
        float[] embedding,
        string embeddingModelId,
        IReadOnlyList<Voiceprint> voiceprints,
        AmbiguityPolicy policy)
        => MatchWithRechecks(embedding, embeddingModelId, voiceprints, policy, recheckEmbeddings: []);

    /// <summary>
    /// Matches original plus caller-supplied recheck embeddings, up to
    /// <see cref="AmbiguityPolicy.MaxRechecks"/>. Rechecking is bounded and explicit;
    /// no native re-verification hook exists.
    /// </summary>
    public static SpeakerMatch MatchWithRechecks(
        float[] embedding,
        string embeddingModelId,
        IReadOnlyList<Voiceprint> voiceprints,
        AmbiguityPolicy policy,
        IReadOnlyList<float[]> recheckEmbeddings)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModelId);
        ArgumentNullException.ThrowIfNull(voiceprints);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(recheckEmbeddings);
        policy.Validate();

        var candidates = new List<float[]> { embedding };
        candidates.AddRange(recheckEmbeddings.Where(e => e is not null).Take(policy.MaxRechecks));

        SpeakerMatch best = SpeakerMatch.Unknown();
        int reverifiedCount = 0;

        foreach (float[] candidate in candidates)
        {
            if (candidate.Length == 0)
            {
                continue;
            }

            if (reverifiedCount > 0 || !ReferenceEquals(candidate, embedding))
            {
                reverifiedCount++;
            }

            SpeakerMatch current = MatchOnce(candidate, embeddingModelId, voiceprints, policy, reverifiedCount);
            if (IsBetter(current, best))
            {
                best = current with { ReverifiedCount = reverifiedCount };
            }
        }

        return best with { ReverifiedCount = reverifiedCount };
    }

    private static SpeakerMatch MatchOnce(
        float[] embedding,
        string embeddingModelId,
        IReadOnlyList<Voiceprint> voiceprints,
        AmbiguityPolicy policy,
        int reverifiedCount)
    {
        ValidateFinite(embedding);

        var scores = new List<(string PersonId, string DisplayName, double Score)>();
        foreach (Voiceprint voiceprint in voiceprints)
        {
            if (voiceprint is null)
            {
                continue;
            }

            if (voiceprint.Dimension != embedding.Length || voiceprint.Embeddings is null || voiceprint.Embeddings.Count == 0)
            {
                continue;
            }

            if (!string.Equals(voiceprint.EmbeddingModelId, embeddingModelId, StringComparison.Ordinal))
            {
                continue;
            }

            double bestForPerson = double.NegativeInfinity;
            foreach (float[] sample in voiceprint.Embeddings)
            {
                if (sample is null || sample.Length != embedding.Length)
                {
                    continue;
                }

                double score = Cosine(embedding, sample);
                if (score > bestForPerson)
                {
                    bestForPerson = score;
                }
            }

            if (!double.IsNegativeInfinity(bestForPerson))
            {
                scores.Add((voiceprint.PersonId, voiceprint.DisplayName, bestForPerson));
            }
        }

        if (scores.Count == 0)
        {
            return SpeakerMatch.Unknown(reverifiedCount);
        }

        List<(string PersonId, string DisplayName, double Score)> ordered = scores
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.PersonId, StringComparer.Ordinal)
            .ToList();

        double bestScore = ordered[0].Score;
        double secondBest = ordered.Count > 1 ? ordered[1].Score : double.NaN;
        double margin = ordered.Count > 1 ? bestScore - ordered[1].Score : double.PositiveInfinity;

        bool known = bestScore >= policy.MinScore && margin >= policy.MinMargin;
        SpeakerIdentityDecision decision = known
            ? SpeakerIdentityDecision.Known
            : bestScore >= policy.MinScore
                ? SpeakerIdentityDecision.Ambiguous
                : SpeakerIdentityDecision.Unknown;

        return new SpeakerMatch(
            decision,
            ordered[0].PersonId,
            ordered[0].DisplayName,
            bestScore,
            secondBest,
            margin,
            reverifiedCount);
    }

    private static bool IsBetter(SpeakerMatch current, SpeakerMatch best)
    {
        int currentRank = DecisionRank(current.Decision);
        int bestRank = DecisionRank(best.Decision);
        if (currentRank != bestRank)
        {
            return currentRank > bestRank;
        }

        if (double.IsNaN(current.BestScore))
        {
            return false;
        }

        if (double.IsNaN(best.BestScore))
        {
            return true;
        }

        if (current.BestScore != best.BestScore)
        {
            return current.BestScore > best.BestScore;
        }

        if (double.IsNaN(current.Margin))
        {
            return false;
        }

        if (double.IsNaN(best.Margin))
        {
            return true;
        }

        return current.Margin > best.Margin;
    }

    private static int DecisionRank(SpeakerIdentityDecision decision) => decision switch
    {
        SpeakerIdentityDecision.Known => 2,
        SpeakerIdentityDecision.Ambiguous => 1,
        _ => 0,
    };

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            double x = a[i];
            double y = b[i];
            dot += x * y;
            normA += x * x;
            normB += y * y;
        }

        if (normA <= 0.0 || normB <= 0.0)
        {
            return -1.0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
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
