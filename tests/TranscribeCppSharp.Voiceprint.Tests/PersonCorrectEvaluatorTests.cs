namespace TranscribeCppSharp.Voiceprint.Tests;

using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class PersonCorrectEvaluatorTests
{
    [Fact]
    public void KnownScoreIsSeparatedFromPersonCorrect()
    {
        var wrongKnown = new SpeakerMatch(
            SpeakerIdentityDecision.Known,
            "p2",
            "Bob",
            BestScore: 1.0,
            SecondBestScore: 0.0,
            Margin: 1.0,
            ReverifiedCount: 0);

        PersonCorrectEvaluation evaluation = PersonCorrectEvaluator.Evaluate("p1", wrongKnown);

        Assert.False(evaluation.PersonCorrect);
        Assert.False(evaluation.KnownPersonCorrect); // `Known` != real `PersonId`
        Assert.True(evaluation.NonTarget);
        Assert.True(evaluation.FalseAccept);
        Assert.False(evaluation.FalseReject);
        Assert.False(evaluation.TrueReject);
    }

    [Fact]
    public void TargetRejectedIsFalseRejectAndNonTargetRejectedIsTrueReject()
    {
        var rejected = new SpeakerMatch(
            SpeakerIdentityDecision.Ambiguous,
            "p1",
            "Alice",
            BestScore: 0.5,
            SecondBestScore: 0.4,
            Margin: 0.1,
            ReverifiedCount: 0);

        PersonCorrectEvaluation target = PersonCorrectEvaluator.Evaluate("p1", rejected);
        PersonCorrectEvaluation nonTarget = PersonCorrectEvaluator.Evaluate("p2", rejected);

        Assert.True(target.PersonCorrect);
        Assert.False(target.KnownPersonCorrect);
        Assert.True(target.FalseReject);
        Assert.False(target.TrueReject);

        Assert.False(nonTarget.PersonCorrect);
        Assert.True(nonTarget.TrueReject);
        Assert.False(nonTarget.FalseReject);
    }
}
