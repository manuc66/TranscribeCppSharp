namespace TranscribeCppSharp.Cli;

using System.Text;

/// <summary>
/// Makes text that came from a model, or from a file name, safe to print.
/// </summary>
/// <remarks>
/// <para>
/// A terminal interprets control characters. A transcript is whatever the audio
/// contained, and audio is often something the user did not write: a voice note,
/// a meeting recording, a podcast. An escape sequence in it can move the cursor,
/// repaint the screen, set the clipboard (OSC 52, where the terminal allows it)
/// or forge a line that looks like this tool's own output.
/// </para>
/// <para>
/// Only printing is changed. Exported files keep the text as transcribed, where
/// those characters are data rather than instructions to a terminal.
/// </para>
/// </remarks>
internal static class TerminalText
{
    /// <summary>
    /// <paramref name="text"/> with control characters removed, except the line
    /// break and the tab.
    /// </summary>
    /// <param name="text">The text about to be written to a console.</param>
    /// <returns>The printable form.</returns>
    internal static string Safe(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\n' or '\t')
            {
                builder.Append(c);
            }
            else if (char.IsControl(c))
            {
                // ESC, CR, DEL and the C1 range. Replaced by a space rather than
                // dropped, so "a\rb" does not silently become "ab" mid-sentence.
                builder.Append(' ');
            }
            else if (IsBidiControl(c))
            {
                // Invisible by design, and able to make printed text read
                // differently from what it says.
                continue;
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool IsBidiControl(char c)
        => c is '\u200E' or '\u200F' or '\u061C'
            or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
            or '\u2066' or '\u2067' or '\u2068' or '\u2069';
}
