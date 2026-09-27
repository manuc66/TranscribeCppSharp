// Time and text formatting for the console and the exported transcript. Split
// out of the top-level statements so the exact rendering is unit tested: these
// strings are what a user reads, and the WebVTT/JSON consumers parse them.
//
// The helpers are locale-independent by construction (no interpolated numbers,
// only TimeSpan components), so an exported file looks the same everywhere. The
// console lines that do print numbers (sample counts, RTF) intentionally follow
// the current culture, as the README states.

using System.Globalization;
using System.Text.RegularExpressions;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Cli;

internal static class Fmt
{
    /// <summary>Console timestamp: 00:10.5, or 1:02:03.4 once past an hour.</summary>
    internal static string Ts(double totalMs)
    {
        var t = TimeSpan.FromMilliseconds(totalMs);
        int tenths = t.Milliseconds / 100;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{tenths:0}"
            : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{tenths:0}";
    }

    /// <summary>Console duration: 00:11, or 1:02:03 once past an hour.</summary>
    internal static string Dur(double totalSec)
    {
        var t = TimeSpan.FromSeconds(totalSec);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    /// <summary>WebVTT cue timestamp: 00:00:10.500 (always HH:MM:SS.mmm).</summary>
    internal static string VttTs(double totalMs)
    {
        var t = TimeSpan.FromMilliseconds(totalMs);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    /// <summary>Human-readable diarization state, as printed and exported.</summary>
    internal static string DiarizeState(DiarizeMode mode)
        => mode == DiarizeMode.DiarizeModeOff ? "off" : "on";

    /// <summary>Collapses whitespace runs, so one segment stays one line.</summary>
    internal static string Normalize(string s)
        => Regex.Replace(s.Trim(), @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Lines in a number, for the "audio : 176 000 samples" report.</summary>
    internal static string Samples(long count) => string.Create(CultureInfo.CurrentCulture, $"{count:N0}");
}
