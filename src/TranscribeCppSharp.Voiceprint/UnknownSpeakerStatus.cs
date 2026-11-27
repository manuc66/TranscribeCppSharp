namespace TranscribeCppSharp.Voiceprint;

/// <summary>Lifecycle of an anonymous speaker pending identification.</summary>
public enum UnknownSpeakerStatus
{
    /// <summary>No `PersonId`; can be matched to previous unknown cluster.</summary>
    Pending,

    /// <summary>Later identified as a real `PersonId`.</summary>
    Identified,

    /// <summary>Rejected; no identity should be inferred.</summary>
    Rejected,
}
