namespace VoiceprintEval;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Sweeps MinScore/MinMargin over raw eval output. Does not apply thresholds automatically.</summary>
public static class ThresholdCalibrator
{
    private const string CalibrationNote =
        "Provisional on this corpus only. Apply manually. No universal threshold, no production accuracy claim.";

    public static ThresholdCalibration Calibrate(VoiceprintEvalResults results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return Calibrate(results.Results);
    }

    public static ThresholdCalibration Calibrate(List<VoiceprintEvalResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<VoiceprintEvalResult> all = results.ToList();
        List<double> minScoreGrid = Grid(
            -1.0, 1.0, 0.025,
            all.Where(r => r.Finite && double.IsFinite(r.BestScore)).Select(r => r.BestScore));
        List<double> minMarginGrid = Grid(
            0.0, 2.0, 0.025,
            all.Where(r => r.Finite && double.IsFinite(r.Margin)).Select(r => r.Margin));

        var candidates = new List<CalibrationCandidate>(minScoreGrid.Count * minMarginGrid.Count);
        foreach (double minScore in minScoreGrid)
        {
            foreach (double minMargin in minMarginGrid)
            {
                candidates.Add(EvaluateThreshold(all, minScore, minMargin));
            }
        }

        CalibrationCandidate best = candidates
            .OrderBy(c => c.BalancedErrorRate)
            .ThenBy(c => c.Far)
            .ThenBy(c => c.FalseRejectRate)
            .ThenByDescending(c => c.Accuracy)
            .ThenByDescending(c => c.UnknownRejectionRate)
            .ThenBy(c => c.MinScore)
            .ThenBy(c => c.MinMargin)
            .First();

        return new ThresholdCalibration(Language: string.Empty, best, candidates.Count, candidates, CalibrationNote);
    }


    /// <summary>Global calibration + per-language calibration. `""` means unlabeled.</summary>
    public static ThresholdCalibrationByLanguage CalibrateByLanguage(List<VoiceprintEvalResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        ThresholdCalibration global = Calibrate(results);
        List<ThresholdCalibration> byLanguage = results
            .GroupBy(r => r.Language ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Calibrate(g.Key, g.ToList()))
            .ToList();

        return new ThresholdCalibrationByLanguage(global, byLanguage);
    }

    /// <summary>Calibrates one group and labels output externally.</summary>
    public static ThresholdCalibration Calibrate(string name, List<VoiceprintEvalResult> results)
    {
        return Calibrate(results) with { Language = name };
    }

    private static CalibrationCandidate EvaluateThreshold(List<VoiceprintEvalResult> results, double minScore, double minMargin)
    {
        int trueAccept = 0;
        int falseAccept = 0;
        int falseReject = 0;
        int unknownRejected = 0;
        int unknownCount = 0;
        int knownCount = 0;
        int ambiguousCount = 0;
        int trueReject = 0;
        int nonTargetCount = 0;

        foreach (VoiceprintEvalResult result in results)
        {
            bool hasCandidate = result.HasCandidate && result.Finite && double.IsFinite(result.BestScore) && double.IsFinite(result.Margin);
            bool scoreOk = hasCandidate && result.BestScore >= minScore;
            bool marginOk = hasCandidate && result.Margin >= minMargin;
            bool known = scoreOk && marginOk;

            bool predictedCorrect = known && result.PersonCorrect;
            bool nonTarget = !result.PersonCorrect; // wrong known + unknown + no candidate + invalid

            if (!nonTarget)
            {
                // target count tracked via trueAccept + falseReject below
            }
            else
            {
                nonTargetCount++;
            }

            if (known)
            {
                knownCount++;
            }

            if (scoreOk && !known)
            {
                ambiguousCount++;
            }

            if (predictedCorrect)
            {
                trueAccept++;
            }
            else if (known)
            {
                falseAccept++;
            }
            else if (nonTarget)
            {
                trueReject++; // non-target correctly rejected; standard FAR denominator
            }

            if (result.PersonCorrect && !known)
            {
                falseReject++; // target trial rejected; standard FRR numerator
            }

            if (result.ExpectedPersonId is null)
            {
                unknownCount++;
                if (!known)
                {
                    unknownRejected++;
                }
            }
        }

        int total = results.Count;
        int knownTotal = trueAccept + falseAccept;
        int expectedKnown = trueAccept + falseReject;
        int falseAcceptDenominator = falseAccept + trueReject;
        double falseAcceptRateAmongKnown = knownTotal == 0 ? 0.0 : (double)falseAccept / knownTotal;
        // Standard FAR: false accepts / all non-target trials.
        double falseAcceptRate = falseAcceptDenominator == 0 ? 0.0 : (double)falseAccept / falseAcceptDenominator;
        // Standard FRR: false rejects / all target trials.
        double falseRejectRate = expectedKnown == 0 ? 0.0 : (double)falseReject / expectedKnown;
        double unknownRejectionRate = unknownCount == 0 ? double.NaN : (double)unknownRejected / unknownCount;
        double accuracy = total == 0 ? double.NaN : (double)(trueAccept + trueReject) / total;
        double balancedError = (falseAcceptRate + falseRejectRate) / 2.0;

        return new CalibrationCandidate(
            minScore,
            minMargin,
            total,
            trueAccept,
            falseAccept,
            falseReject,
            unknownRejected,
            unknownCount,
            knownCount,
            ambiguousCount,
            trueReject,
            nonTargetCount,
            falseAcceptRate,
            falseRejectRate,
            unknownRejectionRate,
            accuracy,
            balancedError,
            total == 0 ? double.NaN : (double)knownCount / total,
            falseAcceptRateAmongKnown,
            falseAcceptRate,
            falseRejectRate);
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
