using TranscribeCppSharp.Cli;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the console sanitizer: a transcript is model output, so it is
/// whatever the audio contained, and a terminal reads control characters.
/// </summary>
public class TerminalTextTests
{
    [Fact]
    public void EscapeSequencesLoseTheirEscapeAndBecomeText()
    {
        string safe = TerminalText.Safe("before\u001b[2Jafter");

        Assert.DoesNotContain('\u001b', safe);
        Assert.Equal("before [2Jafter", safe);
    }

    [Fact]
    public void AnOscSequenceCannotReachTheClipboardOrTheTitle()
    {
        // ESC ] 52 ; c ; <base64> BEL is the clipboard write on terminals that
        // allow it; ESC ] 0 ; <text> BEL sets the window title.
        string clipboard = TerminalText.Safe("\u001b]52;c;aGVsbG8=\u0007");
        string title = TerminalText.Safe("\u001b]0;pwned\u0007");

        Assert.DoesNotContain('\u001b', clipboard);
        Assert.DoesNotContain('\u0007', clipboard);
        Assert.DoesNotContain('\u001b', title);
        Assert.DoesNotContain('\u0007', title);
    }

    [Fact]
    public void CarriageReturnAndOtherControlsBecomeSpaces()
    {
        // CR would send the cursor to the start of the line and let the rest of
        // the text overwrite what was printed before it.
        Assert.Equal("a b", TerminalText.Safe("a\rb"));
        Assert.Equal("a b", TerminalText.Safe("a\u007fb"));
        Assert.Equal("a b", TerminalText.Safe("a\u0085b"));
    }

    [Fact]
    public void LineBreaksAndTabsSurvive()
    {
        Assert.Equal("one\ntwo\tthree", TerminalText.Safe("one\ntwo\tthree"));
    }

    [Fact]
    public void BidiOverridesAreDropped()
    {
        // Invisible, and able to make the printed text read as something else.
        Assert.Equal("abc", TerminalText.Safe("a\u202eb\u2066c\u2069"));
    }

    [Fact]
    public void OrdinaryTextIsUnchanged()
    {
        const string text = "Étant donné — 你好, 100 % & $HOME";
        Assert.Equal(text, TerminalText.Safe(text));
    }
}
