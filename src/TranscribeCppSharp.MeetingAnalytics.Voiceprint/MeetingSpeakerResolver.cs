namespace TranscribeCppSharp.MeetingAnalytics.Voiceprint;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp;
using TranscribeCppSharp.MeetingAnalytics;
using TranscribeCppSharp.Voiceprint;

/// <summary>
/// Optional voiceprint resolution for anonymous meeting analytics.
/// No `nl`, no `PersonCorrect`, no raw transcript rewrite, no automatic production identity.
/// </summary>
public sealed class MeetingSpeakerResolver
{
    private readonly ISpeakerEmbedder embedder;
    private readonly IVoiceprintStore voiceprintStore;
    private readonly IUnknownSpeakerStore? unknownSpeakerStore;
    private readonly MeetingSpeakerResolutionOptions options;

    /// <summary>Creates a resolver; identity is opt-in, analytics stays anonymous without calling it.</summary>
    public MeetingSpeakerResolver(
        ISpeakerEmbedder embedder,
        IVoiceprintStore voiceprintStore,
        IUnknownSpeakerStore? unknownSpeakerStore,
        MeetingSpeakerResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(voiceprintStore);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        this.embedder = embedder;
        this.voiceprintStore = voiceprintStore;
        this.unknownSpeakerStore = unknownSpeakerStore;
        this.options = options;
    }

    /// <summary>
    /// Resolves per-recording `SpeakerId` to optional `PersonId` / pending `UnknownId`.
    /// Embeddings and rechecks are bounded; unknown persistence is optional.
    /// </summary>
    public async ValueTask<MeetingSpeakerResolution> ResolveAsync(
        ReadOnlyMemory<float> pcm16kMono,
        IReadOnlyList<SpeakerSegmentResult> turns,
        IReadOnlyList<WordResult> words,
        string transcriptId,
        string language = "",
        string condition = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptId);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(words);

        MeetingAnalytics analytics = MeetingAnalyzer.Analyze(turns, words, options.AnalyticsOptions);
        IReadOnlyList<Voiceprint> voiceprints = await voiceprintStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SpeakerSnippet> snippets = SpeakerSnippetExtractor.Extract(
            pcm16kMono.Span,
            turns,
            embedder.SampleRate,
            options.MinTurnMs,
            options.PaddingMs,
            options.MaxSnippetMs,
            options.AudioQuality);

        UnknownSpeakerTracker? tracker = unknownSpeakerStore is not null && options.PersistUnknownSpeakers
            ? new UnknownSpeakerTracker(
                unknownSpeakerStore,
                embedder.ModelId,
                options.IdentityPolicy,
                voiceprintStore,
                options: null,
                modelSha256: options.ModelSha256)
            : null;

        var identities = new List<SpeakerIdentity>();
        var labels = new List<MeetingSpeakerLabel>();
        var unknowns = new List<UnknownSpeaker>();

        foreach (SpeakerTalkStats speaker in analytics.Speakers)
        {
            List<SpeakerSnippet> candidates = snippets
                .Where(s => s.RunSpeakerId == speaker.SpeakerId)
                .OrderByDescending(s => s.Pcm16kMono.Length)
                .ThenBy(s => s.Start)
                .Take(options.MaxSnippetsPerSpeaker)
                .ToList();

            SpeakerMatch match;
            SpeakerSnippet? primary = null;
            float[]? primaryEmbedding = null;

            if (candidates.Count == 0)
            {
                match = SpeakerMatch.Unknown();
            }
            else
            {
                int embeddingCount = Math.Min(candidates.Count, 1 + options.IdentityPolicy.MaxRechecks);
                var embeddings = new List<float[]>(embeddingCount);
                for (int i = 0; i < embeddingCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    embeddings.Add(await embedder.EmbedAsync(candidates[i].Pcm16kMono, cancellationToken).ConfigureAwait(false));
                }

                primary = candidates[0];
                primaryEmbedding = embeddings[0];
                match = VoiceprintMatcher.MatchWithRechecks(
                    primaryEmbedding,
                    embedder.ModelId,
                    voiceprints,
                    options.IdentityPolicy,
                    embeddings.Skip(1).ToArray());
            }

            identities.Add(new SpeakerIdentity(speaker.SpeakerId, match));

            if (match.Decision == SpeakerIdentityDecision.Known)
            {
                labels.Add(new MeetingSpeakerLabel(
                    speaker.SpeakerId,
                    match.PersonId,
                    match.DisplayName,
                    MeetingSpeakerIdentityStatus.Known));
                continue;
            }

            UnknownSpeaker? unknown = null;
            if (tracker is not null && primary is not null && primaryEmbedding is not null)
            {
                unknown = await tracker.RecordUnknownAsync(
                    transcriptId,
                    primary,
                    primaryEmbedding,
                    language,
                    condition,
                    cancellationToken).ConfigureAwait(false);
                unknowns.Add(unknown);
            }

            MeetingSpeakerIdentityStatus status = match.Decision == SpeakerIdentityDecision.Ambiguous
                ? MeetingSpeakerIdentityStatus.Ambiguous
                : MeetingSpeakerIdentityStatus.Unknown;
            labels.Add(new MeetingSpeakerLabel(
                speaker.SpeakerId,
                PersonId: null,
                unknown?.DisplayName,
                status,
                unknown?.UnknownId));
        }

        Dictionary<int, string> names = SpeakerNameMapper.KnownSpeakerNames(identities);
        return new MeetingSpeakerResolution(
            analytics.WithSpeakerLabels(labels),
            identities,
            unknowns,
            names);
    }
}
