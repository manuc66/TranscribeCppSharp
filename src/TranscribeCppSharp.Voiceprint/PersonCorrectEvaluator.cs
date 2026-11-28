namespace TranscribeCppSharp.Voiceprint;

using System;

/// <summary>Separate `PersonCorrect` evaluation. `Known` score != real identity; no `nl`; no `PersonId` invented.</summary>
public static class PersonCorrectEvaluator
{
    /// <summary>
    /// `PersonCorrect` = actual `PersonId` equals expected target `PersonId`.
    /// `KnownPersonCorrect` = `SpeakerMatch.Known` + `PersonCorrect`.
    /// No `SpeakerMatch.PersonCorrect`, no `UnknownSpeaker.PersonCorrect`, no `nl`.
    /// </summary>
    public static PersonCorrectEvaluation Evaluate(string? expectedPersonId, SpeakerMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        bool personCorrect = expectedPersonId is not null
            && match.PersonId is not null
            && string.Equals(expectedPersonId, match.PersonId, StringComparison.Ordinal);
        bool nonTarget = !personCorrect;
        bool known = match.Decision == SpeakerIdentityDecision.Known;
        bool knownPersonCorrect = known && personCorrect;

        return new PersonCorrectEvaluation(
            expectedPersonId,
            match.PersonId,
            match.Decision,
            personCorrect,
            knownPersonCorrect,
            nonTarget,
            known && nonTarget,
            !known && personCorrect,
            !known && nonTarget);
    }
}
