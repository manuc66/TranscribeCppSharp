namespace TranscribeCppSharp.Voiceprint;

using System;

/// <summary>Provenance of an unknown speaker observation. No audio by default; no raw transcript rewrite.</summary>
public sealed record UnknownSpeakerSource(
    string TranscriptId,
    int RunSpeakerId,
    TimeSpan Start,
    TimeSpan End,
    float DiarizationProbability,
    string SnippetSha256,
    string Language,
    string Condition);
