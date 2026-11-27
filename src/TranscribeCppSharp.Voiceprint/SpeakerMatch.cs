namespace TranscribeCppSharp.Voiceprint;

/// <summary>Result of matching an embedding against saved voiceprints.</summary>
/// <param name="Decision">Known/Unknown/Ambiguous under the caller policy.</param>
/// <param name="PersonId">Matched local person id, or null.</param>
/// <param name="DisplayName">Matched display name, or null.</param>
/// <param name="BestScore">Best cosine score, or NaN when no candidate.</param>
/// <param name="SecondBestScore">Second-best person score, or NaN when absent.</param>
/// <param name="Margin">Best minus second best, or NaN when absent.</param>
/// <param name="ReverifiedCount">How many recheck embeddings were considered.</param>
public sealed record SpeakerMatch(
    SpeakerIdentityDecision Decision,
    string? PersonId,
    string? DisplayName,
    double BestScore,
    double SecondBestScore,
    double Margin,
    int ReverifiedCount)
{
    /// <summary>True when caller should re-segment/re-run model before naming.</summary>
    public bool NeedsReverification => Decision != SpeakerIdentityDecision.Known;

    /// <summary>Creates an unknown result.</summary>
    public static SpeakerMatch Unknown(int reverifiedCount = 0) => new(
        SpeakerIdentityDecision.Unknown,
        PersonId: null,
        DisplayName: null,
        BestScore: double.NaN,
        SecondBestScore: double.NaN,
        Margin: double.NaN,
        ReverifiedCount: reverifiedCount);
}
