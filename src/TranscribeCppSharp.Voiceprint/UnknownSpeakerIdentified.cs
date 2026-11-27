namespace TranscribeCppSharp.Voiceprint;

/// <summary>Result of later identifying an anonymous speaker. `Voiceprint` can be used for future `Known` matching.</summary>
public sealed record UnknownSpeakerIdentified(
    UnknownSpeaker UnknownSpeaker,
    Voiceprint Voiceprint);
