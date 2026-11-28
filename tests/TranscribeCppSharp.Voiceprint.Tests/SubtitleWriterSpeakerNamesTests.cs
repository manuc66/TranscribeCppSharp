namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.IO;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class SubtitleWriterSpeakerNamesTests
{
    [Fact]
    public void SharedSubtitleWriterUsesSpeakerNameMapperOutput()
    {
        var identity = new SpeakerIdentity(
            1,
            new SpeakerMatch(
                SpeakerIdentityDecision.Known,
                "person-1",
                "Alice",
                BestScore: 1.0,
                SecondBestScore: double.NaN,
                Margin: double.PositiveInfinity,
                ReverifiedCount: 0));
        var names = SpeakerNameMapper.KnownSpeakerNames([identity]);
        var writer = new StringWriter();

        SubtitleWriter.Write(
            writer,
            SubtitleFormat.Vtt,
            [new SubtitleCue(0, 1000, Speaker: 1, Text: "hello")],
            speakerLabels: true,
            speakerNames: names);

        string vtt = writer.ToString();
        Assert.Contains("<v Alice>hello</v>", vtt);
        Assert.Equal("Alice", names[1]);
    }
}
