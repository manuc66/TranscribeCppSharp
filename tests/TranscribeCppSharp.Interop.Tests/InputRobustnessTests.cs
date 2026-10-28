using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.SharedSource;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// What happens when the input is hostile: a WAV header that lies, subtitle text
/// that contains the syntax of the format it is written to, a transcript with
/// control characters in it.
/// </summary>
/// <remarks>
/// The audit found a real instance of each class (a declared data chunk sized the
/// read buffer, a streamed WebVTT cue wrote raw markup, the console printed model
/// output verbatim). These are the invariants that make the class hard to
/// reintroduce: no allocation sized by a claim, no line the text can forge, and
/// nothing that stops being valid JSON. Deterministic seeds, so a failure is
/// reproducible.
/// </remarks>
public class InputRobustnessTests(ITestOutputHelper output)
{
    private static readonly Regex SrtTiming = new(@"^\d{2}:\d{2}:\d{2},\d{3} --> ", RegexOptions.None);
    private static readonly Regex VttTiming = new(@"^\d{2}:\d{2}:\d{2}\.\d{3} --> ", RegexOptions.None);
    private static readonly Regex AssDialogue = new(@"^Dialogue: 0,\d+:\d{2}:\d{2}\.\d{2},", RegexOptions.None);

    /// <summary>Text that is hostile to each format: markup, timing lines, controls.</summary>
    private static string Hostile(int index) => (index % 5) switch
    {
        0 => "line\n\n2\n00:00:09,000 --> 00:00:10,000\n<script>alert(1)</script>",
        1 => "a < b & c > d </v> <v Speaker 9>",
        2 => "{\\fs99\\c&HFF0000&}override{\\b1}",
        3 => "\u001b[2J\u001b]0;title\u0007 cr\rlf",
        _ => "ordinary sentence " + index.ToString(CultureInfo.InvariantCulture),
    };

    [Fact]
    public void RandomBytesNeverMakeTheWavReaderReadBeyondTheFile()
    {
        using var temp = new TempWorkspace();
        var random = new Random(20261028);
        int parsed = 0;

        for (int i = 0; i < 300; i++)
        {
            byte[] bytes = new byte[random.Next(0, 4096)];
            random.NextBytes(bytes);

            // A third of them carry a RIFF/WAVE skeleton, so the chunk walker is
            // exercised rather than returning on the first four bytes.
            if (i % 3 == 0 && bytes.Length >= 44)
            {
                "RIFF"u8.CopyTo(bytes);
                "WAVE"u8.CopyTo(bytes.AsSpan(8));
                "fmt "u8.CopyTo(bytes.AsSpan(12));
                "data"u8.CopyTo(bytes.AsSpan(36));
            }

            string path = temp.WriteBytes($"fuzz-{i}.wav", bytes);

            WavPcmSource? source;
            try
            {
                source = WavPcmSource.TryOpen(path);
            }
            catch (InvalidDataException)
            {
                // A header the reader refuses is a valid answer.
                continue;
            }

            if (source is null)
            {
                continue;
            }

            parsed++;
            using (source)
            {
                // 16-bit samples: whatever the header claims, the file cannot hold
                // more than half its length in samples.
                Assert.InRange(source.LengthSamples, 0, (bytes.Length / 2) + 1);

                try
                {
                    _ = source.ReadWindow(0, 64);
                }
                catch (EndOfStreamException)
                {
                    // The header lied about the data chunk; the clamp is what kept
                    // the read bounded to the file.
                }
            }
        }

        output.WriteLine($"{parsed} of 300 random files were accepted as WAV");
    }

    [Fact]
    public void ADeclaredDataChunkNeverSizesMoreThanTheFileHolds()
    {
        using var temp = new TempWorkspace();
        int[] sizes = [0, 1, 2, 43, 44, 1024, int.MaxValue, -1];
        short[] channels = [0, 1, 2, 8, 32767];
        int[] rates = [16_000, 44_100, 0, int.MaxValue];

        int index = 0;
        foreach (int size in sizes)
        {
            foreach (short channel in channels)
            {
                foreach (int rate in rates)
                {
                    byte[] header = BuildWavHeader(size, channel, rate);
                    string path = temp.WriteBytes($"h-{index++}.wav", header);

                    WavPcmSource? source;
                    try
                    {
                        source = WavPcmSource.TryOpen(path);
                    }
                    catch (InvalidDataException)
                    {
                        continue;
                    }

                    if (source is null)
                    {
                        continue;
                    }

                    using (source)
                    {
                        Assert.InRange(source.LengthSamples, 0, (header.Length / 2) + 1);
                    }
                }
            }
        }

        Assert.True(index > 0);
    }

    [Theory]
    [InlineData(SubtitleFormat.Srt)]
    [InlineData(SubtitleFormat.Vtt)]
    [InlineData(SubtitleFormat.Ass)]
    public void HostileTextCannotAddACue(SubtitleFormat format)
    {
        SubtitleCue[] cues = [.. Enumerable.Range(0, 20).Select(i => new SubtitleCue(
            i * 1000,
            (i * 1000) + 900,
            i % 3,
            Hostile(i)))];

        var names = new Dictionary<int, string> { [0] = "a\nb --> c", [1] = "<v x>", [2] = "{}" };
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        SubtitleWriter.Write(writer, format, cues, speakerLabels: true, speakerNames: names);

        string[] lines = writer.ToString().Split('\n');
        int timings = format switch
        {
            SubtitleFormat.Srt => lines.Count(l => SrtTiming.IsMatch(l)),
            SubtitleFormat.Vtt => lines.Count(l => VttTiming.IsMatch(l)),
            _ => lines.Count(l => AssDialogue.IsMatch(l)),
        };

        // One cue in, one timing out: the text cannot pose as another one.
        Assert.Equal(cues.Length, timings);

        // HTML-ish markup in the text is escaped where the format has markup.
        if (format == SubtitleFormat.Vtt)
        {
            Assert.DoesNotContain("<script", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HostileTextCannotInjectIntoTheHtmlDocument()
    {
        HtmlTranscriptSegment[] segments = [.. Enumerable.Range(0, 10).Select(i => new HtmlTranscriptSegment(
            i * 1000,
            (i * 1000) + 900,
            i % 2,
            Hostile(i)))];

        var info = new HtmlTranscriptInfo(
            "/audio/a<script>.wav",
            TimeSpan.FromSeconds(10),
            "model\"onload=\"x",
            "\"><script>alert(1)</script>",
            "on",
            300,
            new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified));

        var writer = new StringWriter(CultureInfo.InvariantCulture);
        HtmlTranscriptWriter.Write(
            writer,
            info,
            segments,
            speakerNames: new Dictionary<int, string> { [0] = "<img src=x>", [1] = "\"onmouseover=\"y" });

        string content = writer.ToString();

        Assert.DoesNotContain("<script", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", content, StringComparison.Ordinal);
        Assert.DoesNotContain("onload=\"x", content, StringComparison.Ordinal);
        Assert.Contains("&lt;script", content, StringComparison.Ordinal);
        Assert.Equal(segments.Length, content.Split("<tr><td class=\"ln\">").Length - 1);
    }

    [Fact]
    public void AnyTextSurvivesTheJsonDocument()
    {
        var lines = new List<TranscriptLine>();
        for (int i = 0; i < 20; i++)
        {
            lines.Add(new TranscriptLine(i * 1000, (i * 1000) + 900, i % 3, Hostile(i)));
        }

        var info = new TranscriptInfo(
            "/audio/a.wav",
            TimeSpan.FromSeconds(20),
            "m",
            "en",
            TranscribeCppSharp.Interop.DiarizeMode.DiarizeModeOff,
            300,
            new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified));

        var writer = new StringWriter(CultureInfo.InvariantCulture);
        TranscriptWriter.WriteFooter(writer, TranscriptFormat.Json, info, lines);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        JsonElement segments = document.RootElement.GetProperty("segments");
        Assert.Equal(lines.Count, segments.GetArrayLength());
        for (int i = 0; i < lines.Count; i++)
        {
            Assert.Equal(lines[i].Text, segments[i].GetProperty("text").GetString());
        }
    }

    [Fact]
    public void ModelOutputCannotDriveTheTerminal()
    {
        // Defensive restatement of TerminalText's contract at the point a
        // transcript is printed.
        string printed = TerminalText.Safe("ok\u001b[2J\u001b]52;c;ZXZpbA==\u0007 done\r");

        Assert.DoesNotContain('\u001b', printed);
        Assert.DoesNotContain('\u0007', printed);
        Assert.DoesNotContain('\r', printed);
    }

    /// <summary>A 44-byte WAV header with the given data size, channels and rate.</summary>
    private static byte[] BuildWavHeader(int dataSize, short channels, int sampleRate)
    {
        byte[] header = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BitConverter.TryWriteBytes(header.AsSpan(4), 36 + Math.Max(0, dataSize));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        BitConverter.TryWriteBytes(header.AsSpan(16), 16);
        BitConverter.TryWriteBytes(header.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(header.AsSpan(22), channels);
        BitConverter.TryWriteBytes(header.AsSpan(24), sampleRate);
        BitConverter.TryWriteBytes(header.AsSpan(28), sampleRate);
        BitConverter.TryWriteBytes(header.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(header.AsSpan(34), (short)16);
        Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BitConverter.TryWriteBytes(header.AsSpan(40), dataSize);
        return header;
    }
}
