namespace VoiceprintEval;

using System;

/// <summary>Local annotated corpus. Paths are resolved relative to the manifest.</summary>
public sealed class VoiceprintEvalCorpus
{
    public List<VoiceprintEvalPerson> Persons { get; init; } = [];
    public List<string> Unknown { get; init; } = [];
    public List<VoiceprintEvalUnknownCase> UnknownCases { get; init; } = [];
}

/// <summary>One saved local person with enrollment and same-speaker test audio.</summary>
public sealed class VoiceprintEvalPerson
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public List<string> Enrollment { get; init; } = [];
    public List<string> Test { get; init; } = [];
}

/// <summary>Annotated unknown test. Paths resolve relative to manifest.</summary>
public sealed class VoiceprintEvalUnknownCase
{
    public string Path { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
}

/// <summary>Raw eval result before applying a calibrated threshold.</summary>
public sealed record VoiceprintEvalResult(
    string Test,
    string? ExpectedPersonId,
    string? ActualPersonId,
    bool PersonCorrect,
    bool HasCandidate,
    bool Finite,
    string EmbeddingModelId,
    int Dimension,
    int PcmSamples,
    double DurationMs,
    double BestScore,
    double SecondBestScore,
    double Margin,
    double EmbeddingWallMs,
    double EmbeddingCpuMs,
    long EmbeddingAllocatedBytes,
    int EnrollmentCount,
    string Language,
    string Condition);

/// <summary>Raw eval output + run totals. Decisions are calibrated separately.</summary>
public sealed record VoiceprintEvalResults(
    string EmbeddingModelId,
    VoiceprintEvalSummary Summary,
    List<VoiceprintEvalResult> Results);

/// <summary>Run totals. No accuracy claim.</summary>
public sealed record VoiceprintEvalSummary(
    int Total,
    int KnownPersonTests,
    int UnknownTests,
    int PersonCorrectCount,
    int UnknownCandidateCount,
    int EmbeddingsTotal,
    double AudioSecondsTotal,
    double EmbeddingWallMsTotal,
    double EmbeddingCpuMsTotal,
    long EmbeddingAllocatedBytesTotal,
    double PersonCorrectRate,
    double UnknownCandidateRate,
    double MeanBestScore,
    double MeanMargin,
    double EmbeddingWallMsPerAudioSecond,
    double EmbeddingCpuMsPerAudioSecond);

/// <summary>One threshold candidate; not production certification.</summary>
public sealed record CalibrationCandidate(
    double MinScore,
    double MinMargin,
    int Total,
    int TrueAccept,
    int FalseAccept,
    int FalseReject,
    int UnknownRejected,
    int UnknownCount,
    int KnownCount,
    int AmbiguousCount,
    double FalseAcceptRate,
    double FalseRejectRate,
    double UnknownRejectionRate,
    double Accuracy,
    double BalancedErrorRate,
    double Coverage);

/// <summary>Calibration output. Apply manually; no automatic production threshold.</summary>
public sealed record ThresholdCalibration(
    string Language,
    CalibrationCandidate Best,
    int CandidateCount,
    List<CalibrationCandidate> Candidates,
    string Note);


/// <summary>Global + per-language calibration. `""` means unlabeled.</summary>
public sealed record ThresholdCalibrationByLanguage(
    ThresholdCalibration Global,
    List<ThresholdCalibration> ByLanguage);
