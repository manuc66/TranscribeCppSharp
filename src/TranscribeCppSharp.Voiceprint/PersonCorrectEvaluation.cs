namespace TranscribeCppSharp.Voiceprint;

/// <summary>`PersonCorrect`, `Known`, `Unknown`, `Ambiguous`, `non-target` separated; no `nl`.</summary>
public sealed record PersonCorrectEvaluation(
    string? ExpectedPersonId,
    string? ActualPersonId,
    SpeakerIdentityDecision Decision,
    bool PersonCorrect,
    bool KnownPersonCorrect,
    bool NonTarget,
    bool FalseAccept,
    bool FalseReject,
    bool TrueReject);
