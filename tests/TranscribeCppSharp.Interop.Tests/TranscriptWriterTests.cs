using System.Globalization;
using System.Text.Json;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.SharedSource;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the exported transcript shapes. These files are read by other
/// tools (WebVTT players, whisper-style JSON readers), so the exact layout is
/// part of the contract, not an implementation detail.
/// </summary>
public class TranscriptWriterTests
{
    private static readonly DateTime Generated = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified);

    private static readonly TranscriptLine[] Lines =
    [
        new(0, 10_500, 0, "And so my fellow Americans ask not what your country can do for you."),
        new(10_600, 21_000, 2, "Ask what you can do for your country."),
    ];

    private static readonly TranscriptInfo Info = new(
        "/audio/jfk.wav", TimeSpan.FromSeconds(11), "whisper/whisper-tiny", "en",
        DiarizeMode.DiarizeModeOn, 300, Generated);

    private static readonly HtmlTranscriptSegment[] HtmlSegments =
        Lines.Select(l => new HtmlTranscriptSegment(l.StartMs, l.EndMs, l.Speaker, l.Text)).ToArray();

    private static string Write(Action<TextWriter> body)
    {
        var writer = new StringWriter();
        // CRLF on purpose, whatever the platform: the writers must not take their
        // line endings from the TextWriter they are handed. A Windows CI leg
        // caught this, and with it the fact that the same audio produced a
        // different WebVTT file there than on Linux.
        writer.NewLine = "\r\n";
        body(writer);
        return writer.ToString();
    }

    [Fact]
    public void All_Formats_IgnoreTheWritersOwnNewlineConvention()
    {
        // The same three shapes through a CRLF writer, asserted on the whole
        // document: a stray \r anywhere would make the bytes differ per platform.
        foreach (TranscriptFormat format in new[] { TranscriptFormat.Plain, TranscriptFormat.Vtt, TranscriptFormat.Json, TranscriptFormat.Html })
        {
            string content = Write(w =>
            {
                TranscriptWriter.WriteHeader(w, format, Info);
                foreach (TranscriptLine line in Lines)
                {
                    TranscriptWriter.WriteSegment(w, format, line);
                }

                TranscriptWriter.WriteFooter(w, format, Info, Lines);
            });

            Assert.DoesNotContain("\r", content);
            Assert.EndsWith("\n", content);
        }
    }

    [Fact]
    public void Plain_HeaderRecordsTheRunAndTheSegmentLinesAreTimestamped()
    {
        string content = Write(w =>
        {
            TranscriptInfo info = Info with { ModelLabel = "moss/moss-transcribe-diarize" };
            TranscriptWriter.WriteHeader(w, TranscriptFormat.Plain, info);
            foreach (TranscriptLine line in Lines)
            {
                TranscriptWriter.WriteSegment(w, TranscriptFormat.Plain, line);
            }

            TranscriptWriter.WriteFooter(w, TranscriptFormat.Plain, info, Lines);
        });

        Assert.StartsWith("# TranscribeCppSharp diarization transcript", content);
        Assert.Contains("# source   : /audio/jfk.wav", content);
        Assert.Contains("# audio    : 00:00:11", content);
        Assert.Contains("# model    : moss/moss-transcribe-diarize", content);
        Assert.Contains("# lang     : en   diarize: on   window: 300s", content);
        Assert.Contains("# generated: 2026-01-01 09:00:00", content);
        Assert.Contains("[00:00.0 -> 00:10.5] Speaker 0: And so my fellow Americans ask not what your country can do for you.", content);
        Assert.Contains("[00:10.6 -> 00:21.0] Speaker 2: Ask what you can do for your country.", content);
        Assert.Contains("# done: 2026-01-01 09:00:00", content);
    }

    [Fact]
    public void Plain_HeaderStatesThatDiarizationIsOff()
    {
        string content = Write(w => TranscriptWriter.WriteHeader(
            w, TranscriptFormat.Plain,
            Info with
            {
                AudioPath = "/a.wav",
                Duration = TimeSpan.FromSeconds(1),
                ModelLabel = "whisper/whisper-tiny",
                Diarize = DiarizeMode.DiarizeModeOff,
            }));

        Assert.Contains("diarize: off", content);
    }

    [Fact]
    public void Vtt_StartsWithTheMagicLineAndCarriesTheSpeakerCues()
    {
        string content = Write(w =>
        {
            TranscriptWriter.WriteHeader(w, TranscriptFormat.Vtt, Info);
            foreach (TranscriptLine line in Lines)
            {
                TranscriptWriter.WriteSegment(w, TranscriptFormat.Vtt, line);
            }

            TranscriptWriter.WriteFooter(w, TranscriptFormat.Vtt, Info, Lines);
        });

        string[] lines = content.Split('\n');
        Assert.Equal("WEBVTT", lines[0]);
        Assert.Equal("NOTE source: /audio/jfk.wav", lines[1]);
        Assert.Equal("NOTE audio 00:00:11 model whisper/whisper-tiny lang en diarize on window 300s generated 2026-01-01 09:00:00", lines[2]);
        // A cue timestamp is always HH:MM:SS.mmm, as WebVTT requires.
        Assert.Equal("00:00:00.000 --> 00:00:10.500", lines[4]);
        Assert.Equal("<v Speaker 0>And so my fellow Americans ask not what your country can do for you.</v>", lines[5]);
        Assert.Equal("00:00:10.600 --> 00:00:21.000", lines[7]);
        Assert.Contains("NOTE done 2026-01-01 09:00:00", content);
    }

    [Fact]
    public void Vtt_EscapesMarkupInAStreamedCue()
    {
        // The streaming writer builds the file as windows arrive, so it cannot
        // use the whole-document renderer. It still writes a markup format: an
        // unescaped '<' in model output would be read as a tag by the player.
        string content = Write(w => TranscriptWriter.WriteSegment(
            w, TranscriptFormat.Vtt, new TranscriptLine(0, 1000, 0, "a < b & c > d")));

        Assert.Contains("<v Speaker 0>a &lt; b &amp; c &gt; d</v>", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Vtt_KeepsACueOnOneLine()
    {
        // A cue ends at a blank line. A line break in the text would let the rest
        // of it pose as a timestamp of its own.
        string content = Write(w => TranscriptWriter.WriteSegment(
            w,
            TranscriptFormat.Vtt,
            new TranscriptLine(0, 1000, 0, "first\n\n00:00:05.000 --> 00:00:06.000\nforged")));

        // The newlines are gone, and the ">" of the forged arrow is escaped:
        // there is no line that looks like a cue timestamp.
        Assert.Contains(
            content.Split('\n'),
            line => line.Contains("first", StringComparison.Ordinal)
                && line.Contains("forged", StringComparison.Ordinal));
        Assert.DoesNotContain(
            content.Split('\n'),
            line => line.StartsWith("00:00:05.000", StringComparison.Ordinal));
    }

    [Fact]
    public void Vtt_NoteStaysOneCommentWhenThePathContainsALineBreak()
    {
        TranscriptInfo info = Info with { AudioPath = "/audio/a\n\n00:00:01.000 --> 00:00:02.000\nx.wav" };

        string content = Write(w => TranscriptWriter.WriteHeader(w, TranscriptFormat.Vtt, info));

        // Two NOTES, still two lines: the path cannot end the comment block or
        // add a cue of its own.
        Assert.Equal(2, content.Split('\n').Count(l => l.StartsWith("NOTE", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            content.Split('\n'),
            line => line.StartsWith("00:00:01.000", StringComparison.Ordinal));
    }

    [Fact]
    public void Plain_KeepsASegmentOnOneLine()
    {
        string content = Write(w => TranscriptWriter.WriteSegment(
            w, TranscriptFormat.Plain, new TranscriptLine(0, 1000, 0, "first\n# done: 2099-01-01 00:00:00")));

        Assert.Contains("Speaker 0: first # done: 2099-01-01 00:00:00", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\n# done:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Vtt_TimestampsUseHoursEvenForShortAudio()
    {
        string content = Write(w => TranscriptWriter.WriteSegment(
            w, TranscriptFormat.Vtt, new TranscriptLine(3_723_456, 3_724_000, 1, "x")));

        Assert.Contains("01:02:03.456 --> 01:02:04.000", content);
    }

    [Fact]
    public void Json_IsValidJsonWithTheDocumentedShape()
    {
        string content = Write(w => TranscriptWriter.WriteFooter(w, TranscriptFormat.Json, Info, Lines));

        // Parsed, not pattern-matched: a file that is not valid JSON is the bug
        // this guards against.
        using JsonDocument doc = JsonDocument.Parse(content);
        Assert.Equal("en", doc.RootElement.GetProperty("language").GetString());
        Assert.Contains("And so my fellow Americans", doc.RootElement.GetProperty("text").GetString()!);

        JsonElement[] segments = doc.RootElement.GetProperty("segments").EnumerateArray().ToArray();
        Assert.Equal(2, segments.Length);
        // Times are in seconds, not milliseconds.
        Assert.Equal(0d, segments[0].GetProperty("start").GetDouble());
        Assert.Equal(10.5d, segments[0].GetProperty("end").GetDouble());
        Assert.Equal(0, segments[0].GetProperty("speaker").GetInt32());
        Assert.Equal(2, segments[1].GetProperty("speaker").GetInt32());
        Assert.Equal("Ask what you can do for your country.", segments[1].GetProperty("text").GetString());
    }

    [Fact]
    public void Json_WithoutSegments_IsStillAValidDocument()
    {
        string content = Write(w => TranscriptWriter.WriteFooter(
            w, TranscriptFormat.Json, Info with { Language = "fr" }, []));

        using JsonDocument doc = JsonDocument.Parse(content);
        Assert.Equal("fr", doc.RootElement.GetProperty("language").GetString());
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("text").GetString());
        Assert.Empty(doc.RootElement.GetProperty("segments").EnumerateArray());
    }

    [Fact]
    public void Json_KeepsADotAsTheDecimalSeparatorInEveryCulture()
    {
        // The machine running the tests may use ',' as its decimal separator
        // (fr_BE here): a hand-built number would produce a file no JSON reader
        // accepts, so the writer must not format numbers with the current culture.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-BE");
            string content = Write(w => TranscriptWriter.WriteFooter(
                w, TranscriptFormat.Json, Info, [new TranscriptLine(1500, 10_500, 0, "x")]));

            Assert.Contains("\"end\":10.5", content);
            using JsonDocument doc = JsonDocument.Parse(content);
            Assert.Equal(10.5d, doc.RootElement.GetProperty("segments")[0].GetProperty("end").GetDouble());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Json_EscapesTextThatWouldBreakTheDocument()
    {
        string content = Write(w => TranscriptWriter.WriteFooter(
            w, TranscriptFormat.Json, Info, [new TranscriptLine(0, 1, 0, "he said \"hi\"\nand left")]));

        using JsonDocument doc = JsonDocument.Parse(content);
        Assert.Equal("he said \"hi\"\nand left", doc.RootElement.GetProperty("segments")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Json_HeaderIsEmpty_BecauseTheDocumentIsWrittenOnceAtTheEnd()
    {
        string content = Write(w => TranscriptWriter.WriteHeader(
            w, TranscriptFormat.Json,
            Info with { AudioPath = "/a.wav", Duration = TimeSpan.FromSeconds(1) }));

        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void Html_IsAStandaloneNumberedDocumentWithTimestampsAndSpeakers()
    {
        string content = Write(w => TranscriptWriter.WriteFooter(w, TranscriptFormat.Html, Info, Lines));

        Assert.StartsWith("<!DOCTYPE html>", content);
        Assert.Contains("<html lang=\"en\">", content);
        Assert.Contains("<meta charset=\"utf-8\">", content);
        Assert.Contains("<title>jfk.wav — transcript</title>", content);
        Assert.EndsWith("</html>\n", content);

        // One row per segment, numbered from 1: the numbers are literal cells,
        // not a CSS counter, so a word processor's HTML import keeps them.
        Assert.Contains("<tr><td class=\"ln\">1</td><td class=\"text\">", content);
        Assert.Contains("<tr><td class=\"ln\">2</td><td class=\"text\">", content);

        // A stamp on the first segment and on the speaker change.
        Assert.Contains("[00:00:00]</span> <span class=\"speaker\">Speaker 0:</span> And so my fellow", content);
        Assert.Contains("[00:00:10]</span> <span class=\"speaker\">Speaker 2:</span> Ask what you can do", content);

        // The run is recorded, so the file says what produced it.
        Assert.Contains("source: /audio/jfk.wav", content);
        Assert.Contains("audio: 00:11 · model whisper/whisper-tiny · language en · diarize on · window 300s · segments 2", content);
        Assert.Contains("generated: 2026-01-01 09:00:00", content);
    }

    [Fact]
    public void Html_StampsAtMostOncePerMinute()
    {
        // Same speaker throughout: the stamps come from the 60 s rule, not from
        // a speaker change. 0 s, 59 s (too soon), 60 s (stamped), 130 s (stamped).
        TranscriptLine[] lines =
        [
            new(0, 1_000, 0, "a"),
            new(59_000, 60_000, 0, "b"),
            new(60_000, 61_000, 0, "c"),
            new(130_000, 131_000, 0, "d"),
        ];

        string content = Write(w => TranscriptWriter.WriteFooter(w, TranscriptFormat.Html, Info, lines));

        Assert.Contains("[00:00:00]", content);
        Assert.DoesNotContain("[00:00:59]", content);
        Assert.Contains("[00:01:00]", content);
        Assert.Contains("[00:02:10]", content);
    }

    [Fact]
    public void Html_EscapesTextInsteadOfLettingItBecomeMarkup()
    {
        string content = Write(w => TranscriptWriter.WriteFooter(
            w, TranscriptFormat.Html, Info,
            [new TranscriptLine(0, 1, 0, "he said \"<script>alert(1)</script>\" & left")]));

        Assert.DoesNotContain("<script>", content);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", content);
        Assert.Contains("&quot;", content);
        Assert.Contains("&amp; left", content);
    }

    [Fact]
    public void Html_WithoutSegmentsSaysSoInsteadOfAnEmptyTable()
    {
        string content = Write(w => TranscriptWriter.WriteFooter(w, TranscriptFormat.Html, Info, []));

        Assert.Contains("<p class=\"empty\">No transcript segments were produced.</p>", content);
        Assert.DoesNotContain("<table", content);
    }

    [Fact]
    public void Html_OmitsTheLangAttributeWhenTheLanguageIsAuto()
    {
        // "auto" is what the CLI passes when the model decides for itself; it is
        // not a BCP 47 tag, so the document must not claim it is one.
        string content = Write(w => TranscriptWriter.WriteFooter(
            w, TranscriptFormat.Html, Info with { Language = "auto" }, Lines));

        Assert.Contains("<html>", content);
        Assert.DoesNotContain("lang=\"auto\"", content);
    }

    [Fact]
    public void Html_OmitsTheLanguageWhenAnEntryCarriesNone()
    {
        // A saved history entry whose JSON has no language deserializes to null,
        // even though the record said string. The desktop app passes it straight
        // to the renderer, which must not crash and must not invent an attribute.
        var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        TranscribeCppSharp.SharedSource.HtmlTranscriptWriter.Write(
            writer,
            new TranscribeCppSharp.SharedSource.HtmlTranscriptInfo(
                "/audio/jfk.wav",
                TimeSpan.FromSeconds(11),
                "whisper/web",
                Language: null,
                "on",
                300,
                new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified)),
            HtmlSegments);

        string content = writer.ToString();
        Assert.Contains("<html>", content, StringComparison.Ordinal);
        Assert.DoesNotContain("lang=", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_HeaderIsEmpty_BecauseTheDocumentIsWrittenOnceAtTheEnd()
    {
        string content = Write(w => TranscriptWriter.WriteHeader(
            w, TranscriptFormat.Html,
            Info with { AudioPath = "/a.wav", Duration = TimeSpan.FromSeconds(1) }));

        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void Html_SaysTheDurationIsUnknownWhenItIsNotKnown()
    {
        // The desktop app does not keep the audio file's length. Saying "unknown"
        // is honest; passing the last segment's end would not be the duration.
        var writer = new StringWriter();
        TranscribeCppSharp.SharedSource.HtmlTranscriptWriter.Write(
            writer,
            new TranscribeCppSharp.SharedSource.HtmlTranscriptInfo(
                "/a.wav", null, "whisper-tiny", "fr", "off", 30, Generated),
            []);

        string content = writer.ToString();
        Assert.Contains("audio: unknown · model whisper-tiny · language fr · diarize off · window 30s · segments 0", content);
        Assert.DoesNotContain("\r", content);
    }

    private static string WriteHtml(
        IReadOnlyList<HtmlTranscriptSegment> segments,
        int timestampIntervalMs = HtmlTranscriptWriter.TimestampIntervalMs,
        int pauseThresholdMs = 0,
        IReadOnlyDictionary<int, string>? speakerNames = null)
    {
        var writer = new StringWriter();
        HtmlTranscriptWriter.Write(
            writer,
            new HtmlTranscriptInfo(
                "/audio/jfk.wav", TimeSpan.FromSeconds(11), "whisper/whisper-tiny", "en",
                "on", 300, Generated),
            segments, timestampIntervalMs, pauseThresholdMs, speakerNames);
        return writer.ToString();
    }

    private static int Count(string content, string value)
    {
        int count = 0;
        for (int i = content.IndexOf(value, StringComparison.Ordinal);
             i >= 0;
             i = content.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void Html_MarksAPauseBetweenSegmentsAsOneDotPerSecond()
    {
        HtmlTranscriptSegment[] segments =
        [
            new(0, 5_000, 0, "first"),
            new(8_400, 10_000, 0, "second"),
            new(10_500, 11_000, 0, "third"),
        ];

        string content = WriteHtml(segments, pauseThresholdMs: 1_000);

        // 3.4 s of silence between the first two segments: three dots, in a row of
        // its own so the segment numbering stays one number per segment.
        Assert.Contains(
            "<tr class=\"pause\"><td class=\"ln\"></td><td class=\"text\"><span class=\"pause\">[...]</span></td></tr>",
            content);
        // The 0.5 s gap is below the 1 s threshold and gets no marker.
        Assert.Equal(1, Count(content, "<tr class=\"pause\">"));
        // Numbering is untouched by the unnumbered row.
        Assert.Contains("<td class=\"ln\">2</td>", content);
        Assert.Contains("<td class=\"ln\">3</td>", content);
    }

    [Fact]
    public void Html_DoesNotMarkAPauseShorterThanTheThreshold()
    {
        HtmlTranscriptSegment[] segments =
        [
            new(0, 5_000, 0, "first"),
            new(5_900, 7_000, 0, "second"),
        ];

        string content = WriteHtml(segments, pauseThresholdMs: 1_000);

        Assert.DoesNotContain("class=\"pause\"", content);
    }

    [Fact]
    public void Html_MarksNoPauseWhenTheThresholdIsZero()
    {
        HtmlTranscriptSegment[] segments =
        [
            new(0, 5_000, 0, "first"),
            new(35_000, 36_000, 0, "second"),
        ];

        string content = WriteHtml(segments, pauseThresholdMs: 0);

        Assert.DoesNotContain("class=\"pause\"", content);
    }

    [Fact]
    public void Html_CapsALongPauseAtTenDots()
    {
        // A minute of silence is still a pause marker, not sixty characters of
        // punctuation in the middle of the transcript.
        HtmlTranscriptSegment[] segments =
        [
            new(0, 1_000, 0, "first"),
            new(61_000, 62_000, 0, "second"),
        ];

        string content = WriteHtml(segments, pauseThresholdMs: 1_000);

        Assert.Contains("<span class=\"pause\">[..........]</span>", content);
    }

    [Fact]
    public void Html_UsesTheSpeakerNameWhenOneIsGiven()
    {
        string content = WriteHtml(
            HtmlSegments,
            speakerNames: new Dictionary<int, string> { [0] = "Alice", [2] = "Bob" });

        Assert.Contains("<span class=\"speaker\">Alice:</span>", content);
        Assert.Contains("<span class=\"speaker\">Bob:</span>", content);
        Assert.DoesNotContain("Speaker 0:", content);
        Assert.DoesNotContain("Speaker 2:", content);
    }

    [Fact]
    public void Html_EscapesASpeakerNameThatLooksLikeMarkup()
    {
        // Speaker names are typed in the app, so they are user input in the same
        // document as the transcript.
        string content = WriteHtml(
            HtmlSegments,
            speakerNames: new Dictionary<int, string> { [0] = "<img src=x onerror=alert(1)>" });

        Assert.DoesNotContain("<img", content, StringComparison.Ordinal);
        Assert.Contains("&lt;img", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_FallsBackToTheSpeakerIdForABlankName()
    {
        string content = WriteHtml(
            HtmlSegments,
            speakerNames: new Dictionary<int, string> { [0] = "   " });

        Assert.Contains("<span class=\"speaker\">Speaker 0:</span>", content);
        Assert.Contains("<span class=\"speaker\">Speaker 2:</span>", content);
    }

    [Fact]
    public void Html_TimestampIntervalIsConfigurable()
    {
        HtmlTranscriptSegment[] segments =
        [
            new(0, 1_000, 0, "a"),
            new(30_000, 31_000, 0, "b"),
            new(70_000, 71_000, 0, "c"),
        ];

        // Every 30 s: the stamps land at 0, 30 and 70 (40 s after the last one).
        Assert.Equal(3, Count(WriteHtml(segments, timestampIntervalMs: 30_000), "class=\"ts\""));

        // Zero means no timed stamp at all: only the first segment is stamped.
        Assert.Equal(1, Count(WriteHtml(segments, timestampIntervalMs: 0), "class=\"ts\""));
    }

    [Fact]
    public void Html_StampsOnASpeakerChangeEvenWhenTheIntervalIsOff()
    {
        HtmlTranscriptSegment[] segments =
        [
            new(0, 1_000, 0, "a"),
            new(30_000, 31_000, 1, "b"),
        ];

        string content = WriteHtml(segments, timestampIntervalMs: 0);

        Assert.Equal(2, Count(content, "class=\"ts\""));
        Assert.Contains("[00:00:30]", content);
    }
}

/// <summary>Tests for the time and text rendering the console and the files share.</summary>
public class FmtTests
{
    [Theory]
    [InlineData(0, "00:00.0")]
    [InlineData(10_500, "00:10.5")]
    [InlineData(65_999, "01:05.9")]
    [InlineData(3_600_000, "1:00:00.0")]
    [InlineData(3_723_456, "1:02:03.4")]
    public void Ts_RendersMinutesAndTenths(double ms, string expected)
        => Assert.Equal(expected, Fmt.Ts(ms));

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(11, "00:11")]
    [InlineData(65, "01:05")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    public void Dur_RendersMinutesAndSeconds(double seconds, string expected)
        => Assert.Equal(expected, Fmt.Dur(seconds));

    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(10_500, "00:00:10.500")]
    [InlineData(3_723_456, "01:02:03.456")]
    public void VttTs_AlwaysUsesHoursMinutesSecondsAndMilliseconds(double ms, string expected)
        => Assert.Equal(expected, Fmt.VttTs(ms));

    [Fact]
    public void DiarizeState_IsOffOnlyWhenDiarizationIsOff()
    {
        Assert.Equal("on", Fmt.DiarizeState(DiarizeMode.DiarizeModeOn));
        Assert.Equal("on", Fmt.DiarizeState(DiarizeMode.DiarizeModeDefault));
        Assert.Equal("off", Fmt.DiarizeState(DiarizeMode.DiarizeModeOff));
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceSoASegmentStaysOnOneLine()
    {
        Assert.Equal("ask not what your country can do", Fmt.Normalize("  ask not\n what\t your   country can do  "));
    }

    [Fact]
    public void Normalize_LeavesAnEmptySegmentEmpty()
    {
        Assert.Equal(string.Empty, Fmt.Normalize("   "));
    }

    [Fact]
    public void Samples_GroupsThousands()
    {
        // The console prints the sample count with the current culture's
        // separator, like the rest of the report.
        Assert.Contains("176", Fmt.Samples(176_000));
    }
}
