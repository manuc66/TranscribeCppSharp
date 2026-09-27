#nullable enable

using System;
using System.Globalization;
using System.Text.Json;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.Interop;
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
        foreach (TranscriptFormat format in new[] { TranscriptFormat.Plain, TranscriptFormat.Vtt, TranscriptFormat.Json })
        {
            string content = Write(w =>
            {
                TranscriptWriter.WriteHeader(
                    w, format, "/audio/jfk.wav", TimeSpan.FromSeconds(11),
                    "whisper/whisper-tiny", "en", DiarizeMode.DiarizeModeOn, 300, Generated);
                foreach (TranscriptLine line in Lines)
                {
                    TranscriptWriter.WriteSegment(w, format, line);
                }

                TranscriptWriter.WriteFooter(w, format, "en", Lines, Generated);
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
            TranscriptWriter.WriteHeader(
                w, TranscriptFormat.Plain, "/audio/jfk.wav", TimeSpan.FromSeconds(11),
                "moss/moss-transcribe-diarize", "en", DiarizeMode.DiarizeModeOn, 300, Generated);
            foreach (TranscriptLine line in Lines)
            {
                TranscriptWriter.WriteSegment(w, TranscriptFormat.Plain, line);
            }

            TranscriptWriter.WriteFooter(w, TranscriptFormat.Plain, "en", Lines, Generated);
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
            w, TranscriptFormat.Plain, "/a.wav", TimeSpan.FromSeconds(1), "whisper/whisper-tiny", "en",
            DiarizeMode.DiarizeModeOff, 300, Generated));

        Assert.Contains("diarize: off", content);
    }

    [Fact]
    public void Vtt_StartsWithTheMagicLineAndCarriesTheSpeakerCues()
    {
        string content = Write(w =>
        {
            TranscriptWriter.WriteHeader(
                w, TranscriptFormat.Vtt, "/audio/jfk.wav", TimeSpan.FromSeconds(11),
                "whisper/whisper-tiny", "en", DiarizeMode.DiarizeModeOn, 300, Generated);
            foreach (TranscriptLine line in Lines)
            {
                TranscriptWriter.WriteSegment(w, TranscriptFormat.Vtt, line);
            }

            TranscriptWriter.WriteFooter(w, TranscriptFormat.Vtt, "en", Lines, Generated);
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
    public void Vtt_TimestampsUseHoursEvenForShortAudio()
    {
        string content = Write(w => TranscriptWriter.WriteSegment(
            w, TranscriptFormat.Vtt, new TranscriptLine(3_723_456, 3_724_000, 1, "x")));

        Assert.Contains("01:02:03.456 --> 01:02:04.000", content);
    }

    [Fact]
    public void Json_IsValidJsonWithTheDocumentedShape()
    {
        string content = Write(w => TranscriptWriter.WriteFooter(w, TranscriptFormat.Json, "en", Lines, Generated));

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
        string content = Write(w => TranscriptWriter.WriteFooter(w, TranscriptFormat.Json, "fr", [], Generated));

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
                w, TranscriptFormat.Json, "en", [new TranscriptLine(1500, 10_500, 0, "x")], Generated));

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
            w, TranscriptFormat.Json, "en", [new TranscriptLine(0, 1, 0, "he said \"hi\"\nand left")], Generated));

        using JsonDocument doc = JsonDocument.Parse(content);
        Assert.Equal("he said \"hi\"\nand left", doc.RootElement.GetProperty("segments")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Json_HeaderIsEmpty_BecauseTheDocumentIsWrittenOnceAtTheEnd()
    {
        string content = Write(w => TranscriptWriter.WriteHeader(
            w, TranscriptFormat.Json, "/a.wav", TimeSpan.FromSeconds(1), "whisper/whisper-tiny", "en",
            DiarizeMode.DiarizeModeOn, 300, Generated));

        Assert.Equal(string.Empty, content);
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
