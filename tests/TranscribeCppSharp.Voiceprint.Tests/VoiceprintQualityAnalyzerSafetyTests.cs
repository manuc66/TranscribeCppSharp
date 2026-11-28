namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class VoiceprintQualityAnalyzerSafetyTests
{
    [Fact]
    public void ConsistentEmbeddingsPassBasicQualityNotIdentity()
    {
        Voiceprint voiceprint = Print("p1", "model-v1", 2, [[1f, 0f], [0.98f, 0.02f], [0.99f, 0.01f]], sourceCount: 3);

        VoiceprintQuality quality = VoiceprintQualityAnalyzer.Analyze(
            voiceprint,
            new VoiceprintQualityPolicy(MinSourceSampleCount: 2, MinEmbeddingCount: 2, RequiredDimension: 2, MinNorm: 0.9, MaxNorm: 1.1, MinMeanPairwiseCosine: 0.9, MinSelfMatch: 0.9, MaxOutlierScore: 0.1));

        Assert.True(quality.Passed, quality.Reason);
        Assert.True(quality.Finite);
        Assert.True(quality.SameModel);
        Assert.True(quality.SameDimension);
        Assert.Equal(3, quality.EmbeddingCount);
        Assert.Equal(3, quality.SourceSampleCount);
        Assert.True(quality.MeanPairwiseCosine > 0.9);
        Assert.True(quality.MinSelfMatch > 0.9);
        Assert.True(quality.MaxOutlierScore < 0.1);
        Assert.DoesNotContain("PersonCorrect", typeof(VoiceprintQuality).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void NonFiniteWrongDimensionZeroNormOutlierAndContaminationFail()
    {
        Voiceprint nonFinite = Print("p1", "model-v1", 2, [[1f, 0f], [float.NaN, 0f]]);
        Voiceprint wrongDim = Print("p1", "model-v1", 2, [[1f, 0f], [1f, 0f, 0f]]);
        Voiceprint zero = Print("p1", "model-v1", 2, [[0f, 0f], [0f, 0f]]);
        Voiceprint outlier = Print("p1", "model-v1", 2, [[1f, 0f], [1f, 0f], [0f, 1f]]);
        var policy = new VoiceprintQualityPolicy(MinEmbeddingCount: 2, MinMeanPairwiseCosine: 0.9, MinSelfMatch: 0.9, MaxOutlierScore: 0.1, MinNorm: 0.5);

        VoiceprintQuality nonFiniteQuality = VoiceprintQualityAnalyzer.Analyze(nonFinite, policy);
        VoiceprintQuality wrongDimQuality = VoiceprintQualityAnalyzer.Analyze(wrongDim, policy);
        VoiceprintQuality zeroQuality = VoiceprintQualityAnalyzer.Analyze(zero, policy);
        VoiceprintQuality outlierQuality = VoiceprintQualityAnalyzer.Analyze(outlier, policy);

        Assert.False(nonFiniteQuality.Passed);
        Assert.Contains("non-finite", nonFiniteQuality.Reason);
        Assert.False(wrongDimQuality.SameDimension);
        Assert.False(wrongDimQuality.Passed);
        Assert.False(zeroQuality.Passed);
        Assert.Contains("min-norm", zeroQuality.Reason);
        Assert.False(outlierQuality.Passed);
        Assert.Contains("outlier", outlierQuality.Reason);
    }

    [Fact]
    public void ModelSourceCountsAndEmptyCannotPass()
    {
        Voiceprint empty = Print("p1", "model-v1", 2, []);
        Voiceprint noModel = Print("p1", string.Empty, 2, [[1f, 0f]]);
        Voiceprint one = Print("p1", "model-v1", 2, [[1f, 0f]], sourceCount: 1);

        Assert.False(VoiceprintQualityAnalyzer.Analyze(empty).Passed);
        Assert.False(VoiceprintQualityAnalyzer.Analyze(noModel).Passed);
        Assert.False(VoiceprintQualityAnalyzer.Analyze(one, new VoiceprintQualityPolicy(MinSourceSampleCount: 2, MinEmbeddingCount: 2)).Passed);
    }

    private static Voiceprint Print(string id, string modelId, int dimension, float[][] embeddings, int sourceCount = 1) => new(
        id,
        id,
        modelId,
        dimension,
        embeddings,
        DateTimeOffset.UnixEpoch,
        sourceCount);
}
