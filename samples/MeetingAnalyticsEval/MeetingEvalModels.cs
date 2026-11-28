namespace MeetingAnalyticsEval;

using System.Collections.Generic;
using TranscribeCppSharp.Voiceprint;

/// <summary>Local annotated meeting corpus. Paths resolve relative to the manifest.</summary>
public sealed class MeetingEvalCorpus
{
    public List<MeetingEvalCase> Meetings { get; init; } = [];
}

/// <summary>One meeting and expected per-run speaker identities. Null means unknown.</summary>
public sealed class MeetingEvalCase
{
    public string Audio { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public Dictionary<int, string?> ExpectedSpeakers { get; init; } = [];
}

/// <summary>Raw speaker-row evaluation before threshold calibration.</summary>
public sealed record MeetingEvalSpeakerResult(
    string MeetingId,
    string Language,
    string Condition,
    int RunSpeakerId,
    bool HasExpectation,
    string? ExpectedPersonId,
    string? ActualPersonId,
    bool PersonCorrect,
    bool Target,
    bool NonTarget,
    SpeakerIdentityDecision Decision,
    double BestScore,
    double SecondBestScore,
    double Margin,
    int ReverifiedCount,
    string? UnknownId);

/// <summary>One meeting E2E result.</summary>
public sealed record MeetingEvalResult(
    string MeetingId,
    string Audio,
    string Language,
    string Condition,
    double DurationMs,
    int SpeakerCount,
    int EvaluatedSpeakerCount,
    int TargetCount,
    int NonTargetCount,
    int UnknownCount,
    int PersonCorrectCount,
    int TrueAccept,
    int FalseAccept,
    int FalseReject,
    int TrueReject,
    int UnknownRejected,
    int AmbiguousCount,
    double ResolveWallMs,
    double ResolveCpuMs,
    List<MeetingEvalSpeakerResult> Speakers);

/// <summary>Aggregate E2E metrics. No production accuracy claim.</summary>
public sealed record MeetingEvalSummary(
    int TotalMeetings,
    int TotalSpeakers,
    int EvaluatedSpeakers,
    int TargetCount,
    int NonTargetCount,
    int UnknownCount,
    int PersonCorrectCount,
    int TrueAccept,
    int FalseAccept,
    int FalseReject,
    int TrueReject,
    int UnknownRejected,
    int AmbiguousCount,
    double Far,
    double Frr,
    double UnknownRejectionRate,
    double PersonCorrectRate,
    double Accuracy,
    double BalancedErrorRate,
    double Coverage,
    double AudioSecondsTotal,
    double ResolveWallMsTotal,
    double ResolveCpuMsTotal,
    double ResolveCpuMsPerAudioSecond);

/// <summary>Eval output.</summary>
public sealed record MeetingEvalResults(
    MeetingEvalSummary Summary,
    List<MeetingEvalResult> Meetings);

/// <summary>One calibrated threshold candidate.</summary>
public sealed record MeetingEvalCalibrationCandidate(
    double MinScore,
    double MinMargin,
    int Total,
    int TargetCount,
    int NonTargetCount,
    int UnknownCount,
    int PersonCorrectCount,
    int TrueAccept,
    int FalseAccept,
    int FalseReject,
    int TrueReject,
    int UnknownRejected,
    int KnownCount,
    int AmbiguousCount,
    double Far,
    double Frr,
    double UnknownRejectionRate,
    double Accuracy,
    double BalancedErrorRate,
    double Coverage);

/// <summary>Provisional calibration result.</summary>
public sealed record MeetingEvalCalibration(
    string Group,
    MeetingEvalCalibrationCandidate Best,
    int CandidateCount,
    List<MeetingEvalCalibrationCandidate> Candidates,
    string Note);

/// <summary>Global + per-language + per-condition calibration.</summary>
public sealed record MeetingEvalCalibrationSet(
    MeetingEvalCalibration Global,
    List<MeetingEvalCalibration> ByLanguage,
    List<MeetingEvalCalibration> ByCondition);
