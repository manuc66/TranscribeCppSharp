using System.Globalization;
using TranscribeCppSharp.Models;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// One place the app writes what went wrong, so a failure the UI catches can be
/// read after the fact.
/// </summary>
/// <remarks>
/// The view models turn an exception into a line of status text and drop the
/// rest, and that line cannot be copied. A failure was therefore visible only as
/// a sentence, with no type, no inner exception and no stack trace — the
/// streaming Start error was reported exactly that way and could not be
/// diagnosed from the screen. Every catch that reports a failure also writes it
/// here, in full, to a file the reader can open.
///
/// The file is appended to, never rotated. It sits beside the model cache, under
/// the same directory the Settings tab already names.
/// </remarks>
public static class UiLog
{
    private static readonly object Gate = new();

    /// <summary>
    /// Where failures are written. Null means the default location under the
    /// app's cache directory; tests point it at a temporary file.
    /// </summary>
    public static string? FilePathOverride { get; set; }

    /// <summary>The log file's path.</summary>
    public static string FilePath => FilePathOverride ?? DefaultPath();

    /// <summary>
    /// Appends one failure, with everything the exception carries: type,
    /// message, inner exceptions and stack trace.
    /// </summary>
    /// <param name="what">What was being attempted, in a few words.</param>
    /// <param name="exception">The exception that ended it.</param>
    public static void Failure(string what, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string entry = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {what}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");

        try
        {
            lock (Gate)
            {
                // 0600 in a 0700 directory: the log holds paths and stack traces.
                PrivateFile.AppendAllText(FilePath, entry);
            }
        }
        catch (Exception)
        {
            // Logging must not become a second failure: if the file cannot be
            // written, the caller still reports the message it caught.
        }
    }

    /// <summary>
    /// <c>&lt;cache&gt;/../logs/ui.log</c>, beside the model cache rather than in
    /// it, so the two do not mix.
    /// </summary>
    private static string DefaultPath()
        => Path.Combine(
            Path.GetDirectoryName(ModelStore.CacheRoot())!,
            "logs",
            "ui.log");
}
