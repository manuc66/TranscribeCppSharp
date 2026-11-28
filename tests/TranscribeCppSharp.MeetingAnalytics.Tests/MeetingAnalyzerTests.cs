namespace TranscribeCppSharp.MeetingAnalytics.Tests;

using System;
using System.Linq;
using TranscribeCppSharp;
using TranscribeCppSharp.MeetingAnalytics;
using Xunit;

public sealed class MeetingAnalyzerTests
{
    [Fact]
    public void TalkTimeUsesUnionAndSilenceUsesUnionAcrossSpeakers()
    {
        var turns = new[]
        {
            Turn(1, 0, 4),
            Turn(2, 3, 7),
            Turn(1, 6, 8),
        };

        MeetingAnalytics analytics = MeetingAnalyzer.Analyze(turns, [], new MeetingAnalyticsOptions(TimeSpan.FromSeconds(10)));

        Assert.Equal(TimeSpan.FromSeconds(8), analytics.Summary.TotalSpeechTime);
        Assert.Equal(TimeSpan.FromSeconds(2), analytics.Summary.TotalSilenceTime);
        Assert.Equal(TimeSpan.FromSeconds(2), analytics.Summary.LongestSilence);
        Assert.Equal(TimeSpan.FromSeconds(2), analytics.Summary.TotalOverlapTime);
        Assert.Equal(0.25, analytics.Summary.OverlapRatio, precision: 6);
        Assert.Equal(TimeSpan.FromSeconds(6), analytics.Speakers.Single(s => s.SpeakerId == 1).TalkTime);
        Assert.Equal(TimeSpan.FromSeconds(4), analytics.Speakers.Single(s => s.SpeakerId == 2).TalkTime);
        Assert.Equal(1, analytics.Summary.DominantSpeakerId); // longest talk time
        Assert.Equal(2, analytics.Overlaps.Count);
        Assert.Equal(0, analytics.IgnoredTurnCount);
    }

    [Fact]
    public void InvalidTurnsAndSpeakerZeroAreIgnoredNotInvented()
    {
        var turns = new[]
        {
            Turn(0, 0, 5),
            Turn(1, 5, 5),
            Turn(1, 6, 4),
            Turn(2, 1, 2),
        };

        MeetingAnalytics analytics = MeetingAnalyzer.Analyze(turns, [], new MeetingAnalyticsOptions(TimeSpan.FromSeconds(3)));

        Assert.Equal(3, analytics.IgnoredTurnCount);
        Assert.Single(analytics.Speakers);
        Assert.Equal(2, analytics.Speakers[0].SpeakerId);
        Assert.Equal(TimeSpan.FromSeconds(1), analytics.Summary.TotalSpeechTime);
        Assert.Equal(TimeSpan.FromSeconds(2), analytics.Summary.TotalSilenceTime);
    }

    [Fact]
    public void EmptyTurnsProduceEmptyHonestAnalytics()
    {
        MeetingAnalytics analytics = MeetingAnalyzer.Analyze([], [], new MeetingAnalyticsOptions(TimeSpan.FromSeconds(10)));

        Assert.Empty(analytics.Speakers);
        Assert.Empty(analytics.Overlaps);
        Assert.Empty(analytics.Silences);
        Assert.Equal(TimeSpan.Zero, analytics.Summary.TotalSpeechTime);
        Assert.Equal(TimeSpan.FromSeconds(10), analytics.Summary.TotalSilenceTime);
        Assert.Null(analytics.Summary.DominantSpeakerId);
    }

    [Fact]
    public void InterruptionAndBackchannelAreHeuristicNotIdentity()
    {
        var turns = new[]
        {
            Turn(1, 0, 10),
            Turn(2, 3, 4), // short backchannel
            Turn(3, 7, 12), // interruption
        };
        var options = new MeetingAnalyticsOptions(
            TimeSpan.FromSeconds(12),
            MinInterruptedSpeech: TimeSpan.FromMilliseconds(500),
            BackchannelMaxDuration: TimeSpan.FromMilliseconds(1500));

        MeetingAnalytics analytics = MeetingAnalyzer.Analyze(turns, [], options);

        InterruptionEvent backchannel = Assert.Single(analytics.Backchannels);
        Assert.Equal(2, backchannel.InterruptingSpeakerId);
        Assert.Equal(InterruptionKind.Backchannel, backchannel.Kind);
        InterruptionEvent interruption = Assert.Single(analytics.Interruptions);
        Assert.Equal(3, interruption.InterruptingSpeakerId);
        Assert.Equal(1, interruption.InterruptedSpeakerId);
        Assert.Equal(InterruptionKind.Interruption, interruption.Kind);
    }

    [Fact]
    public void WordsAreAssignedByLargestOverlapAndWordsPerMinuteIsComputed()
    {
        var turns = new[] { Turn(1, 0, 5), Turn(2, 5, 10) };
        var words = new[]
        {
            new WordResult(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "one"),
            new WordResult(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(7), "two"),
            new WordResult(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8), "three"),
        };

        MeetingAnalytics analytics = MeetingAnalyzer.Analyze(turns, words, new MeetingAnalyticsOptions(TimeSpan.FromSeconds(10)));

        SpeakerTalkStats speaker1 = analytics.Speakers.Single(s => s.SpeakerId == 1);
        SpeakerTalkStats speaker2 = analytics.Speakers.Single(s => s.SpeakerId == 2);
        Assert.Equal(1, speaker1.WordCount); // tie at word boundary goes lower id
        Assert.Equal(2, speaker2.WordCount);
        Assert.Equal(12.0, speaker1.WordsPerMinute, precision: 6); // 1 word / 5 s
        Assert.Equal(24.0, speaker2.WordsPerMinute, precision: 6); // 2 words / 5 s
    }

    [Fact]
    public void TranscriptOverloadUsesSpeakerSegmentsAndWords()
    {
        var transcript = new Transcript
        {
            SpeakerSegments = [Turn(1, 0, 2), Turn(2, 2, 4)],
            Words = [new WordResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello")],
        };

        MeetingAnalytics analytics = MeetingAnalyzer.Analyze(transcript);

        Assert.Equal(2, analytics.Speakers.Count);
        Assert.Equal(1, analytics.Speakers.Single(s => s.SpeakerId == 1).WordCount);
        Assert.Equal(TimeSpan.FromSeconds(4), analytics.Summary.Duration);
    }

    [Fact]
    public void SpeakerChangesAndSilenceThresholdsAreExplicit()
    {
        var turns = new[] { Turn(1, 0, 2), Turn(1, 2, 3), Turn(2, 3, 5) };
        var options = new MeetingAnalyticsOptions(TimeSpan.FromSeconds(6), MinSilence: TimeSpan.FromMilliseconds(1500));

        MeetingAnalytics analytics = MeetingAnalyzer.Analyze(turns, [], options);

        SpeakerChangeEvent change = Assert.Single(analytics.SpeakerChanges);
        Assert.Equal(TimeSpan.FromSeconds(3), change.At);
        Assert.Equal(1, change.FromSpeakerId);
        Assert.Equal(2, change.ToSpeakerId);
        Assert.Empty(analytics.Silences); // 1 s tail is below 1.5 s
        Assert.Equal(TimeSpan.FromSeconds(1), analytics.Summary.TotalSilenceTime);
        Assert.Equal(TimeSpan.FromSeconds(1), analytics.Summary.LongestSilence);
    }

    [Fact]
    public void MinOverlapFiltersEventsAndSummaryConsistently()
    {
        var turns = new[] { Turn(1, 0, 4), Turn(2, 3, 5) };
        var options = new MeetingAnalyticsOptions(TimeSpan.FromSeconds(5), MinOverlap: TimeSpan.FromSeconds(2));

        MeetingAnalytics analytics = MeetingAnalyzer.Analyze(turns, [], options);

        Assert.Empty(analytics.Overlaps);
        Assert.Equal(TimeSpan.Zero, analytics.Summary.TotalOverlapTime);
    }

    [Fact]
    public void ExportersWriteJsonCsvAndMarkdown()
    {
        MeetingAnalytics analytics = MeetingAnalyzer.Analyze([Turn(1, 0, 5)], [], new MeetingAnalyticsOptions(TimeSpan.FromSeconds(5)));

        string json = MeetingAnalyticsJson.Write(analytics);
        string csv = MeetingAnalyticsCsv.Write(analytics);
        string markdown = MeetingAnalyticsMarkdown.Write(analytics);

        Assert.Contains("\"speakers\"", json);
        Assert.Contains("SpeakerId,TalkMs", csv);
        Assert.Contains("| Speaker |", markdown);
        Assert.Contains("Speaker 1", markdown);
    }

    private static SpeakerSegmentResult Turn(int speakerId, double startSeconds, double endSeconds)
        => new(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds), speakerId, float.NaN);
}
