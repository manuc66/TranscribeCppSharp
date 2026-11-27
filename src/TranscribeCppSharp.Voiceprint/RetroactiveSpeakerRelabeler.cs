namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.Linq;
using TranscribeCppSharp.Audio;

/// <summary>Builds retroactive `speakerNames` for a transcript; **raw transcript is not rewritten**.</summary>
public static class RetroactiveSpeakerRelabeler
{
    /// <summary>
    /// Returns names keyed by numeric per-run `SpeakerId` for `transcriptId`.
    /// Only `Identified` `UnknownSpeaker` sources in this transcript are named. `Speaker 0` not named.
    /// `originalSegments` not modified. Original `SpeakerId` not remapped to `PersonId`.
    /// </summary>
    public static Dictionary<int, string> NamesForTranscript(
        string transcriptId,
        IReadOnlyList<MergedSegment> originalSegments,
        IReadOnlyList<UnknownSpeaker> identified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptId);
        ArgumentNullException.ThrowIfNull(originalSegments);
        ArgumentNullException.ThrowIfNull(identified);

        var names = new Dictionary<int, string>();

        var speakerIds = originalSegments
            .Select(s => s.SpeakerId)
            .Where(id => id != 0)
            .Distinct()
            .ToHashSet();

        foreach (UnknownSpeaker speaker in identified)
        {
            if (speaker is null
                || speaker.Status != UnknownSpeakerStatus.Identified
                || string.IsNullOrWhiteSpace(speaker.PersonId)
                || string.IsNullOrWhiteSpace(speaker.DisplayName))
            {
                continue;
            }

            foreach (UnknownSpeakerSource source in speaker.Sources)
            {
                if (!string.Equals(source.TranscriptId, transcriptId, StringComparison.Ordinal)
                    || source.RunSpeakerId == 0
                    || !speakerIds.Contains(source.RunSpeakerId))
                {
                    continue;
                }

                names[source.RunSpeakerId] = speaker.DisplayName.Trim();
            }
        }

        return names;
    }

    /// <summary>Original segments unchanged + `speakerNames`; feed shared `SubtitleWriter` / `HtmlTranscriptWriter`.</summary>
    public static (IReadOnlyList<MergedSegment> OriginalSegments, Dictionary<int, string> SpeakerNames) NamesOnly(
        string transcriptId,
        IReadOnlyList<MergedSegment> originalSegments,
        IReadOnlyList<UnknownSpeaker> identified)
        => (originalSegments, NamesForTranscript(transcriptId, originalSegments, identified));
}
