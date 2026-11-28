namespace TranscribeCppSharp.Voiceprint;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Measures embedding-set quality. No identity verification, no `nl`, no `PersonCorrect`.</summary>
public static class VoiceprintQualityAnalyzer
{
    /// <summary>Analyzes finite norms, self-consistency, outliers. No audio re-reading.</summary>
    public static VoiceprintQuality Analyze(Voiceprint voiceprint, VoiceprintQualityPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(voiceprint);
        policy ??= new VoiceprintQualityPolicy();
        policy.Validate();

        List<float[]> samples = voiceprint.Embeddings?.Where(e => e is not null).ToList() ?? [];
        int dimension = voiceprint.Dimension;
        var reasons = new List<string>();

        bool finite = true;
        bool sameDimension = true;
        var norms = new List<double>();
        var normalized = new List<double[]>();

        if (samples.Count == 0)
        {
            reasons.Add("no-embeddings");
            finite = false;
            sameDimension = false;
        }

        foreach (float[] sample in samples)
        {
            if (sample.Length != dimension || (policy.RequiredDimension > 0 && sample.Length != policy.RequiredDimension))
            {
                sameDimension = false;
                reasons.Add("dimension");
                continue;
            }

            double norm = 0.0;
            bool sampleFinite = true;
            foreach (float value in sample)
            {
                if (!float.IsFinite(value))
                {
                    finite = false;
                    sampleFinite = false;
                }
                else
                {
                    norm += value * value;
                }
            }

            norm = Math.Sqrt(norm);
            if (!sampleFinite || !double.IsFinite(norm))
            {
                finite = false;
            }

            norms.Add(norm);
            normalized.Add(sampleFinite ? sample.Select(v => norm <= 0.0 ? 0.0 : v / norm).ToArray() : new double[sample.Length]);
        }

        if (dimension <= 0)
        {
            sameDimension = false;
            reasons.Add("dimension");
        }

        if (policy.RequiredDimension > 0 && dimension != policy.RequiredDimension)
        {
            sameDimension = false;
            reasons.Add("dimension");
        }

        if (policy.RequireFinite && !finite)
        {
            reasons.Add("non-finite");
        }

        if (policy.RequireSameDimension && !sameDimension)
        {
            reasons.Add("dimension");
        }

        bool sameModel = !string.IsNullOrWhiteSpace(voiceprint.EmbeddingModelId);
        if (policy.RequireSameModel && !sameModel)
        {
            reasons.Add("model");
        }

        if (voiceprint.SourceSampleCount < policy.MinSourceSampleCount)
        {
            reasons.Add("source-sample-count");
        }

        if (samples.Count < policy.MinEmbeddingCount)
        {
            reasons.Add("embedding-count");
        }

        double minNorm = norms.Count == 0 ? 0.0 : norms.Min();
        double maxNorm = norms.Count == 0 ? 0.0 : norms.Max();
        double meanNorm = norms.Count == 0 ? 0.0 : norms.Average();

        if (norms.Count > 0 && minNorm + 1e-12 < policy.MinNorm)
        {
            reasons.Add("min-norm");
        }

        if (norms.Count > 0 && maxNorm - 1e-12 > policy.MaxNorm)
        {
            reasons.Add("max-norm");
        }

        // Compute pairwise + outlier over normalized valid-dimension samples.
        var valid = normalized.Where((n, i) => i < samples.Count && samples[i].Length == dimension && samples[i].All(float.IsFinite)).ToList();
        double meanPairwise = 1.0;
        double minSelfMatch = 1.0;
        double maxOutlier = 0.0;

        if (valid.Count >= 2)
        {
            var pairs = new List<double>();
            for (int i = 0; i < valid.Count; i++)
            {
                for (int j = i + 1; j < valid.Count; j++)
                {
                    pairs.Add(Cosine(valid[i], valid[j]));
                }
            }

            meanPairwise = pairs.Count == 0 ? 1.0 : pairs.Average();
            if (meanPairwise + 1e-12 < policy.MinMeanPairwiseCosine)
            {
                reasons.Add("pairwise");
            }
        }
        else if (!double.IsNegativeInfinity(policy.MinMeanPairwiseCosine))
        {
            reasons.Add("pairwise");
        }

        if (valid.Count >= 2)
        {
            double[] centroid = new double[dimension];
            foreach (double[] sample in valid)
            {
                for (int i = 0; i < dimension; i++)
                {
                    centroid[i] += sample[i];
                }
            }

            for (int i = 0; i < dimension; i++)
            {
                centroid[i] /= valid.Count;
            }

            var selfMatches = new List<double>();
            var outliers = new List<double>();
            for (int i = 0; i < valid.Count; i++)
            {
                double[] othersCentroid = new double[dimension];
                if (valid.Count >= 2)
                {
                    for (int k = 0; k < valid.Count; k++)
                    {
                        if (k == i)
                        {
                            continue;
                        }

                        for (int d = 0; d < dimension; d++)
                        {
                            othersCentroid[d] += valid[k][d];
                        }
                    }

                    for (int d = 0; d < dimension; d++)
                    {
                        othersCentroid[d] /= valid.Count - 1;
                    }

                    selfMatches.Add(Cosine(valid[i], othersCentroid));
                }

                outliers.Add(1.0 - Cosine(valid[i], centroid));
            }

            minSelfMatch = selfMatches.Count == 0 ? 1.0 : selfMatches.Min();
            maxOutlier = outliers.Count == 0 ? 0.0 : outliers.Max();

            if (minSelfMatch + 1e-12 < policy.MinSelfMatch)
            {
                reasons.Add("self-match");
            }

            if (maxOutlier - 1e-12 > policy.MaxOutlierScore)
            {
                reasons.Add("outlier");
            }
        }
        else if (!double.IsNegativeInfinity(policy.MinSelfMatch))
        {
            reasons.Add("self-match");
        }

        return new VoiceprintQuality(
            reasons.Count == 0,
            reasons.Count == 0 ? string.Empty : string.Join(',', reasons.Distinct(StringComparer.Ordinal)),
            dimension,
            samples.Count,
            voiceprint.SourceSampleCount,
            finite,
            sameModel,
            sameDimension,
            minNorm,
            maxNorm,
            meanNorm,
            meanPairwise,
            minSelfMatch,
            maxOutlier,
            voiceprint.EmbeddingModelId);
    }

    private static double Cosine(double[] a, double[] b)
    {
        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;
        int length = Math.Min(a.Length, b.Length);

        for (int i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0.0 || normB <= 0.0)
        {
            return -1.0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
