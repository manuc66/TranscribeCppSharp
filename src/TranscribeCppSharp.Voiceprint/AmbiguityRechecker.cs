namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Bounded ambiguity re-check for speaker identity.
/// </summary>
/// <remarks>
/// This rechecks **speaker embeddings on alternative crops of the same snippet**.
/// It does **not** re-run diarization, does not remap numeric <c>SpeakerId</c>,
/// does not fuse votes beyond <see cref="VoiceprintMatcher.MatchWithRechecks"/>,
/// does not filter noise/overlap/short audio, and does not loop.
/// </remarks>
public sealed class AmbiguityRechecker
{
    private readonly ISpeakerEmbedder embedder;

    /// <summary>Creates a rechecker over a caller-owned local embedder.</summary>
    public AmbiguityRechecker(ISpeakerEmbedder embedder)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        this.embedder = embedder;
    }

    /// <summary>
    /// Matches a snippet; if not <see cref="SpeakerIdentityDecision.Known"/>, computes
    /// bounded alternative-crop embeddings and calls <see cref="VoiceprintMatcher.MatchWithRechecks"/>.
    /// Sequential calls; embedder must not be shared concurrently.
    /// </summary>
    public async ValueTask<SpeakerMatch> RecheckAsync(
        SpeakerSnippet snippet,
        string embeddingModelId,
        IReadOnlyList<Voiceprint> voiceprints,
        AmbiguityPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModelId);
        ArgumentNullException.ThrowIfNull(voiceprints);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        float[] initial = await embedder.EmbedAsync(snippet.Pcm16kMono, cancellationToken).ConfigureAwait(false);

        SpeakerMatch first = VoiceprintMatcher.Match(initial, embeddingModelId, voiceprints, policy);
        if (first.Decision == SpeakerIdentityDecision.Known || policy.MaxRechecks == 0 || snippet.Pcm16kMono.Length < 2)
        {
            return first;
        }

        var recheckEmbeddings = new List<float[]>();
        foreach (float[] crop in AlternativeCrops(snippet.Pcm16kMono, policy.MaxRechecks))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recheckEmbeddings.Add(await embedder.EmbedAsync(crop, cancellationToken).ConfigureAwait(false));
        }

        return VoiceprintMatcher.MatchWithRechecks(initial, embeddingModelId, voiceprints, policy, recheckEmbeddings);
    }

    /// <summary>Alternative crops; no diarization re-run, no noise filtering, no `nl` claims.</summary>
    public static IEnumerable<float[]> AlternativeCrops(float[] pcm16kMono, int maxRechecks)
    {
        ArgumentNullException.ThrowIfNull(pcm16kMono);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRechecks);

        if (pcm16kMono.Length < 2 || maxRechecks == 0)
        {
            yield break;
        }

        // Trim edges; then center crop. Extra rechecks reuse center crop with smaller trim.
        float[][] crops =
        [
            Trim(pcm16kMono, 0.125),
            Trim(pcm16kMono, 0.25),
            Trim(pcm16kMono, 0.0625),
            pcm16kMono.ToArray(),
        ];

        int yielded = 0;
        foreach (float[] crop in crops)
        {
            if (crop.Length == 0)
            {
                continue;
            }

            yield return crop;
            yielded++;
            if (yielded >= maxRechecks)
            {
                yield break;
            }
        }
    }

    private static float[] Trim(float[] pcm, double fraction)
    {
        int trim = (int)Math.Floor(pcm.Length * fraction);
        int start = Math.Clamp(trim, 0, pcm.Length - 1);
        int end = Math.Clamp(pcm.Length - trim, start + 1, pcm.Length);
        return pcm[start..end];
    }
}
