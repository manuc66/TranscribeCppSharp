namespace TranscribeCppSharp.Voiceprint;

/// <summary>One per-run speaker id mapped to optional identity. Numeric id is not stable identity.</summary>
public sealed record SpeakerIdentity(
    int RunSpeakerId,
    SpeakerMatch Match)
{
    /// <summary>Matched caller-owned person id, or null.</summary>
    public string? PersonId => Match.PersonId;

    /// <summary>Matched display name, or null.</summary>
    public string? DisplayName => Match.DisplayName;

    /// <summary>Known/Unknown/Ambiguous.</summary>
    public SpeakerIdentityDecision Decision => Match.Decision;

    /// <summary>Caller should recheck.</summary>
    public bool NeedsReverification => Match.NeedsReverification;
}
