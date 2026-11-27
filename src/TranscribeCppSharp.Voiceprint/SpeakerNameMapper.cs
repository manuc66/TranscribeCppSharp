namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using TranscribeCppSharp.Audio;

/// <summary>Builds `speakerNames` for <c>SubtitleWriter</c>/`HtmlTranscriptWriter`; raw transcript unchanged.</summary>
public static class SpeakerNameMapper
{
    /// <summary>
    /// Numeric per-run `SpeakerId` -> display name. Only <see cref="SpeakerIdentityDecision.Known"/>
    /// identities are named; unknown/ambiguous keep `Speaker N`. `Speaker 0` not named.
    /// </summary>
    public static Dictionary<int, string> KnownSpeakerNames(IEnumerable<SpeakerIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        var names = new Dictionary<int, string>();
        foreach (SpeakerIdentity identity in identities)
        {
            if (identity.RunSpeakerId == 0 || identity.Decision != SpeakerIdentityDecision.Known)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(identity.DisplayName))
            {
                continue;
            }

            names[identity.RunSpeakerId] = identity.DisplayName.Trim();
        }

        return names;
    }

    /// <summary>Feeds names after `SpeakerAssignment`; no re-mapping of numeric `SpeakerId`.</summary>
    public static Dictionary<int, string> KnownSpeakerNamesForSegments(
        IEnumerable<MergedSegment> segments,
        IEnumerable<SpeakerIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(identities);

        // `segments` intentionally not required for mapping; names keyed by per-run numeric id.
        // Kept for call sites that run SpeakerAssignment first. No probability propagation.
        _ = segments;
        return KnownSpeakerNames(identities);
    }
}
