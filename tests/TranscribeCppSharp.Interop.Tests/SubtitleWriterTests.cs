namespace TranscribeCppSharp.Interop.Tests;

using System.Globalization;
using TranscribeCppSharp.SharedSource;
using Xunit;

/// <summary>
/// The subtitle renderer, exercised on its own. The same source is compiled into
/// the CLI, the server and the GUI, so these assertions cover the bytes all three
/// write.
/// </summary>
public sealed class SubtitleWriterTests
{
    private static readonly SubtitleCue[] Cues =
    [
        new(0, 6250, 0, "Hello there."),
        new(6250, 12_500, 2, "  General   Kenobi.  "),
    ];

    private static string Render(SubtitleFormat format, bool speakerLabels, SubtitleCue[]? cues = null)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        SubtitleWriter.Write(writer, format, cues ?? Cues, speakerLabels, title: "a.wav — whisper-tiny");
        return writer.ToString();
    }

    [Fact]
    public void Srt_NumbersCuesAndUsesCommaMilliseconds()
    {
        string text = Render(SubtitleFormat.Srt, speakerLabels: false);

        Assert.Equal(
            "1\n00:00:00,000 --> 00:00:06,250\nHello there.\n\n"
            + "2\n00:00:06,250 --> 00:00:12,500\nGeneral Kenobi.\n\n",
            text);
    }

    [Fact]
    public void Srt_PrefixesTheSpeakerOnlyWhenAsked()
    {
        string text = Render(SubtitleFormat.Srt, speakerLabels: true);

        Assert.Contains("Speaker 0: Hello there.", text, StringComparison.Ordinal);
        Assert.Contains("Speaker 2: General Kenobi.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Vtt_StartsWithTheHeaderAndUsesDotMilliseconds()
    {
        string text = Render(SubtitleFormat.Vtt, speakerLabels: false);

        Assert.StartsWith("WEBVTT\nNOTE a.wav — whisper-tiny\n\n", text, StringComparison.Ordinal);
        Assert.Contains("00:00:00.000 --> 00:00:06.250\nHello there.\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Vtt_UsesVoiceTagsForSpeakersAndEscapesMarkup()
    {
        string text = Render(SubtitleFormat.Vtt, speakerLabels: true);

        Assert.Contains("<v Speaker 2>General Kenobi.</v>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Vtt_EscapesAngleBracketsAndAmpersands()
    {
        string text = Render(
            SubtitleFormat.Vtt,
            speakerLabels: false,
            [new SubtitleCue(0, 1000, 0, "a < b & c > d")]);

        Assert.Contains("a &lt; b &amp; c &gt; d", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ass_HasAScriptHeaderAndCentisecondTimestamps()
    {
        string text = Render(SubtitleFormat.Ass, speakerLabels: false);

        Assert.StartsWith("[Script Info]\nTitle: a.wav — whisper-tiny\nScriptType: v4.00+\n", text, StringComparison.Ordinal);
        Assert.Contains("[V4+ Styles]", text, StringComparison.Ordinal);
        Assert.Contains("[Events]", text, StringComparison.Ordinal);
        Assert.Contains(
            "Dialogue: 0,0:00:00.00,0:00:06.25,Default,,0,0,0,,Hello there.",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ass_PrefixesTheSpeakerAndEscapesOverrideBraces()
    {
        string text = Render(
            SubtitleFormat.Ass,
            speakerLabels: true,
            [new SubtitleCue(0, 1000, 3, "keep {this} literal")]);

        Assert.Contains("Speaker 3: keep \\{this\\} literal", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_speaker_replaces_the_numeric_label()
    {
        var names = new Dictionary<int, string> { [2] = "Alice" };
        var writer = new StringWriter(CultureInfo.InvariantCulture);

        SubtitleWriter.Write(
            writer,
            SubtitleFormat.Srt,
            [new SubtitleCue(0, 1000, 2, "hello")],
            speakerLabels: true,
            speakerNames: names);

        Assert.Contains("Alice: hello", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_speaker_is_used_in_the_vtt_voice_tag()
    {
        var names = new Dictionary<int, string> { [2] = "Alice" };
        var writer = new StringWriter(CultureInfo.InvariantCulture);

        SubtitleWriter.Write(
            writer,
            SubtitleFormat.Vtt,
            [new SubtitleCue(0, 1000, 2, "hello")],
            speakerLabels: true,
            speakerNames: names);

        Assert.Contains("<v Alice>hello</v>", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_name_falls_back_to_the_speaker_number()
    {
        var names = new Dictionary<int, string> { [2] = "   " };
        var writer = new StringWriter(CultureInfo.InvariantCulture);

        SubtitleWriter.Write(
            writer,
            SubtitleFormat.Srt,
            [new SubtitleCue(0, 1000, 2, "hello")],
            speakerLabels: true,
            speakerNames: names);

        Assert.Contains("Speaker 2: hello", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_transcript_writes_only_the_header_that_the_format_needs()
    {
        Assert.Equal(string.Empty, Render(SubtitleFormat.Srt, speakerLabels: false, []));
        Assert.Equal("WEBVTT\nNOTE a.wav — whisper-tiny\n\n", Render(SubtitleFormat.Vtt, speakerLabels: false, []));
        Assert.Contains("[Events]", Render(SubtitleFormat.Ass, speakerLabels: false, []), StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_cues_are_skipped_rather_than_emitted_empty()
    {
        string text = Render(
            SubtitleFormat.Srt,
            speakerLabels: false,
            [new SubtitleCue(0, 1000, 0, "  "), new SubtitleCue(1000, 2000, 0, "after")]);

        Assert.Equal("1\n00:00:01,000 --> 00:00:02,000\nafter\n\n", text);
    }

    [Fact]
    public void Negative_times_are_clamped_to_zero()
    {
        string text = Render(SubtitleFormat.Srt, speakerLabels: false, [new SubtitleCue(-500, 1000, 0, "x")]);

        Assert.StartsWith("1\n00:00:00,000 --> 00:00:01,000\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Extensions_match_the_format()
    {
        Assert.Equal("srt", SubtitleWriter.Extension(SubtitleFormat.Srt));
        Assert.Equal("vtt", SubtitleWriter.Extension(SubtitleFormat.Vtt));
        Assert.Equal("ass", SubtitleWriter.Extension(SubtitleFormat.Ass));
    }
}
