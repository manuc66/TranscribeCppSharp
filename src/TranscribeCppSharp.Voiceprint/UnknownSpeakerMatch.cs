namespace TranscribeCppSharp.Voiceprint;

/// <summary>Match against a previous anonymous cluster. **Not identity verification.**</summary>
public sealed record UnknownSpeakerMatch(
    bool Matched,
    string? UnknownId,
    double BestScore,
    double SecondBestScore,
    double Margin,
    int ReverifiedCount)
{
    /// <summary>No previous pending cluster.</summary>
    public static UnknownSpeakerMatch Unknown() => new(
        Matched: false,
        UnknownId: null,
        BestScore: double.NaN,
        SecondBestScore: double.NaN,
        Margin: double.NaN,
        ReverifiedCount: 0);
}
