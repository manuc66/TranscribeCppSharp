namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.Collections.Generic;
using VoiceprintEval;
using Xunit;

public sealed class ThresholdCalibratorSafetyTests
{
    [Fact]
    public void FarAndFrrUseStandardTargetNonTargetDenominators()
    {
        // 1 target accepted, 1 target rejected, 1 wrong-known non-target accepted, 1 unknown accepted, 1 unknown rejected.
        var results = new List<VoiceprintEvalResult>
        {
            Row(expected: "p1", actual: "p1", personCorrect: true, hasCandidate: true, bestScore: 1.0, margin: 1.0),
            Row(expected: "p1", actual: "p1", personCorrect: true, hasCandidate: true, bestScore: 0.1, margin: 0.0), // FRR
            Row(expected: "p1", actual: "p2", personCorrect: false, hasCandidate: true, bestScore: 1.0, margin: 1.0), // FAR + non-target, not FRR
            Row(expected: null, actual: "p1", personCorrect: false, hasCandidate: true, bestScore: 1.0, margin: 1.0), // FAR + non-target
            Row(expected: null, actual: null, personCorrect: false, hasCandidate: false, bestScore: double.NaN, margin: double.NaN), // rejected non-target
        };

        ThresholdCalibration calibration = ThresholdCalibrator.Calibrate(results);
        CalibrationCandidate best = calibration.Best;

        // Some thresholds can choose different candidates; inspect candidate at 0.5/0.5 explicitly.
        CalibrationCandidate candidate = calibration.Candidates.Find(c => c.MinScore == 0.5 && c.MinMargin == 0.5)
            ?? throw new InvalidOperationException("missing 0.5 candidate");

        Assert.Equal(1, candidate.TrueAccept);
        Assert.Equal(1, candidate.FalseReject);
        Assert.Equal(2, candidate.FalseAccept);
        Assert.Equal(1, candidate.TrueReject);
        Assert.Equal(1, candidate.UnknownRejected);
        Assert.Equal(3, candidate.NonTargetCount);
        // FAR = false accepts / all non-target, not false accepts / known.
        Assert.Equal(2.0 / 3.0, candidate.Far, precision: 10);
        Assert.Equal(candidate.FalseAcceptRate, candidate.Far);
        // FRR = target rejected / all target trials.
        Assert.Equal(0.5, candidate.Frr);
        Assert.Equal(candidate.FalseRejectRate, candidate.Frr);
        // Legacy alias `falseAcceptRateAmongKnown` is not standard FAR.
        Assert.Equal(2.0 / 3.0, candidate.FalseAcceptRateAmongKnown, precision: 10);
        Assert.Equal((candidate.Far + candidate.Frr) / 2.0, candidate.BalancedErrorRate);
    }

    private static VoiceprintEvalResult Row(string? expected, string? actual, bool personCorrect, bool hasCandidate, double bestScore, double margin) => new(
        "test.wav",
        expected,
        actual,
        personCorrect,
        hasCandidate,
        Finite: true,
        "model-v1",
        Dimension: 2,
        PcmSamples: 16000,
        DurationMs: 1000.0,
        bestScore,
        SecondBestScore: 0.0,
        margin,
        EmbeddingWallMs: 1.0,
        EmbeddingCpuMs: 1.0,
        EmbeddingAllocatedBytes: 0,
        EnrollmentCount: 1,
        Language: string.Empty,
        Condition: string.Empty);
}
