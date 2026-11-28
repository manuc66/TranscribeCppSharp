namespace MeetingAnalyticsEval.Tests;

using System;
using System.Collections.Generic;
using MeetingAnalyticsEval;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class MeetingEvalCalibratorTests
{
    [Fact]
    public void FarAndFrrUseTargetNonTargetDenominators()
    {
        var rows = new List<MeetingEvalSpeakerResult>
        {
            Row("m", 1, "p1", "p1", 1.0, 1.0),
            Row("m", 1, "p1", "p1", 0.1, 0.0),
            Row("m", 1, "p1", "p2", 1.0, 1.0),
            Row("m", 1, null, "p1", 1.0, 1.0),
            Row("m", 1, null, null, double.NaN, double.NaN),
        };

        MeetingEvalCalibration calibration = MeetingEvalCalibrator.Calibrate(rows);
        MeetingEvalCalibrationCandidate candidate = calibration.Candidates.Find(c => c.MinScore == 0.5 && c.MinMargin == 0.5)
            ?? throw new InvalidOperationException("missing candidate");

        Assert.Equal(1, candidate.TrueAccept);
        Assert.Equal(1, candidate.FalseReject);
        Assert.Equal(2, candidate.FalseAccept);
        Assert.Equal(1, candidate.TrueReject);
        Assert.Equal(1, candidate.UnknownRejected);
        Assert.Equal(2, candidate.NonTargetCount);
        Assert.Equal(2.0 / 3.0, candidate.Far, precision: 10);
        Assert.Equal(0.5, candidate.Frr);
        Assert.Equal((candidate.Far + candidate.Frr) / 2.0, candidate.BalancedErrorRate);
    }

    [Fact]
    public void UnannotatedRowsAreIgnoredAndLowMarginIsAmbiguous()
    {
        var rows = new List<MeetingEvalSpeakerResult>
        {
            Row("m", 1, "p1", "p1", 0.9, 0.0),
            Row("m", 2, "p2", "p2", 1.0, 1.0, hasExpectation: false),
        };

        MeetingEvalCalibration calibration = MeetingEvalCalibrator.Calibrate(rows);
        MeetingEvalCalibrationCandidate candidate = calibration.Candidates.Find(c => c.MinScore == 0.5 && c.MinMargin == 0.5)
            ?? throw new InvalidOperationException("missing candidate");

        Assert.Equal(1, candidate.Total);
        Assert.Equal(0, candidate.TrueAccept);
        Assert.Equal(1, candidate.FalseReject);
        Assert.Equal(1, candidate.AmbiguousCount);
    }

    private static MeetingEvalSpeakerResult Row(
        string meetingId,
        int speakerId,
        string? expected,
        string? actual,
        double bestScore,
        double margin,
        bool hasExpectation = true)
    {
        bool target = hasExpectation && expected is not null;
        bool nonTarget = hasExpectation && !target;
        bool personCorrect = target && string.Equals(expected, actual, StringComparison.Ordinal);
        return new MeetingEvalSpeakerResult(
            meetingId,
            speakerId,
            hasExpectation,
            expected,
            actual,
            personCorrect,
            target,
            nonTarget,
            SpeakerIdentityDecision.Unknown,
            bestScore,
            double.NaN,
            margin,
            ReverifiedCount: 0,
            UnknownId: null);
    }
}
