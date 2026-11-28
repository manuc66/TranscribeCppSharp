namespace TranscribeCppSharp.Voiceprint;

/// <summary>Audio quality of a speaker snippet. Quality != speaker identity.</summary>
public sealed record AudioQuality(
    int SampleRate,
    TimeSpan Duration,
    int SampleCount,
    bool Finite,
    double Rms,
    double Peak,
    double ClippingRatio,
    double SilenceRatio,
    double NonSilenceRatio,
    double SnrDb,
    bool NoiseKnown,
    bool SpeechKnown,
    bool OverlapKnown,
    bool SpeechDetected,
    bool Overlap,
    bool Passed,
    string Reason);
