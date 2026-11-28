namespace TranscribeCppSharp.Voiceprint;

using System;

/// <summary>One audio snippet for a diarized speaker turn.</summary>
/// <param name="RunSpeakerId">Numeric per-run id from transcribe.cpp; not a stable identity.</param>
/// <param name="Start">Start time in source audio.</param>
/// <param name="End">End time in source audio.</param>
/// <param name="Pcm16kMono">16 kHz mono float PCM slice.</param>
/// <param name="DiarizationProbability">Diarization confidence if model produces it; otherwise NaN.</param>
/// <param name="Quality">Optional audio quality; not identity.</param>
public sealed record SpeakerSnippet(
    int RunSpeakerId,
    TimeSpan Start,
    TimeSpan End,
    float[] Pcm16kMono,
    float DiarizationProbability,
    AudioQuality? Quality = null);
