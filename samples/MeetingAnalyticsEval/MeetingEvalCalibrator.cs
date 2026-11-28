namespace MeetingAnalyticsEval;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Sweeps MinScore/MinMargin over raw E2E speaker rows. No automatic production threshold.</summary>
public static class MeetingEvalCalibrator
{
    private const string Note =
        "Provisional on this corpus only. Apply manually. No universal threshold, no production accuracy claim.";

    public static MeetingEvalCalibration Calibrate(IReadOnlyList<MeetingEvalSpeakerResult> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        List<MeetingEvalSpeakerResult> evaluated = rows.Where(r => r.HasExpectation).ToList();

        List<double> scoreGrid = Grid(-1.0, 1.0, 0.025, evaluated.Where(r => double.IsFinite(r.BestScore)).Select(r => r.BestScore));
        List<double> marginGrid = Grid(0.0, 2.0, 0.025, evaluated.Where(r => double.IsFinite(r.Margin)).Select(r => r.Margin));
        var candidates = new List<MeetingEvalCalibrationCandidate>(scoreGrid.Count * marginGrid.Count);

        foreach (double minScore in scoreGrid)
        {
            foreach (double minMargin in marginGrid)
            {
                candidates.Add(Evaluate(evaluated, minScore, minMargin));
            }
        }

        MeetingEvalCalibrationCandidate best = candidates
            .OrderBy(c => c.BalancedErrorRate)
            .ThenBy(c => c.Far)
            .ThenBy(c => c.Frr)
            .ThenByDescending(c => c.Accuracy)
            .ThenByDescending(c => c.UnknownRejectionRate)
            .ThenBy(c => c.MinScore)
            .ThenBy(c => c.MinMargin)
            .First();

        return new MeetingEvalCalibration(best, candidates.Count, candidates, Note);
    }

    private static MeetingEvalCalibrationCandidate Evaluate(List<MeetingEvalSpeakerResult> rows, double minScore, double minMargin)
    {
        int targetCount = 0;
        int nonTargetCount = 0;
        int unknownCount = 0;
        int personCorrectCount = 0;
        int trueAccept = 0;
        int falseAccept = 0;
        int falseReject = 0;
        int trueReject = 0;
        int unknownRejected = 0;
        int knownCount = 0;
        int ambiguousCount = 0;

        foreach (MeetingEvalSpeakerResult row in rows)
        {
            bool target = row.Target;
            bool nonTarget = row.NonTarget;
            bool hasCandidate = double.IsFinite(row.BestScore) && double.IsFinite(row.Margin);
            bool scoreOk = hasCandidate && row.BestScore >= minScore;
            bool marginOk = hasCandidate && row.Margin >= minMargin;
            bool known = scoreOk && marginOk;

            if (target)
            {
                targetCount++;
            }

            if (nonTarget)
            {
                nonTargetCount++;
            }

            if (row.ExpectedPersonId is null)
            {
                unknownCount++;
            }

            if (row.PersonCorrect)
            {
                personCorrectCount++;
            }

            if (known)
            {
                knownCount++;
            }

            if (scoreOk && !known)
            {
                ambiguousCount++;
            }

            if (known && row.PersonCorrect)
            {
                trueAccept++;
            }
            else if (known)
            {
                falseAccept++;
            }
            else if (target)
            {
                falseReject++;
            }
            else if (nonTarget)
            {
                trueReject++;
            }

            if (row.ExpectedPersonId is null && !known)
            {
                unknownRejected++;
            }
        }

        int total = rows.Count;
        double far = falseAccept + trueReject == 0 ? 0.0 : (double)falseAccept / (falseAccept + trueReject);
        double frr = trueAccept + falseReject == 0 ? 0.0 : (double)falseReject / (trueAccept + falseReject);
        double unknownRejectionRate = unknownCount == 0 ? double.NaN : (double)unknownRejected / unknownCount;
        double personCorrectRate = targetCount == 0 ? double.NaN : (double)personCorrectCount / targetCount;
        double accuracy = total == 0 ? double.NaN : (double)(trueAccept + trueReject) / total;

        return new MeetingEvalCalibrationCandidate(
            minScore,
            minMargin,
            total,
            targetCount,
            nonTargetCount,
            unknownCount,
            personCorrectCount,
            trueAccept,
            falseAccept,
            falseReject,
            trueReject,
            unknownRejected,
            knownCount,
            ambiguousCount,
            far,
            frr,
            unknownRejectionRate,
            accuracy,
            (far + frr) / 2.0,
            total == 0 ? double.NaN : (double)knownCount / total);
    }

    private static List<double> Grid(double min, double max, double step, IEnumerable<double> observed)
    {
        var values = new SortedSet<double> { min, max, 0.0 };
        for (double value = min; value <= max + 1e-9; value += step)
        {
            values.Add(Math.Round(Math.Clamp(value, min, max), 6));
        }

        foreach (double value in observed)
        {
            values.Add(Math.Round(Math.Clamp(value, min, max), 6));
        }

        return values.ToList();
    }
}
