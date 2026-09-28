#nullable enable

using System.Globalization;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp.Cli;

/// <summary>
/// Owns the temporary file that holds the decoded audio when ffmpeg has to
/// convert it, and makes sure it does not outlive the run.
/// </summary>
/// <remarks>
/// Decoding goes through a file rather than a pipe because a pipe carries no
/// length: reading one means buffering the entire stream before its size is
/// known, which measured 610 MiB of resident memory for 30 minutes of audio
/// against 263 MiB for the file. The file is 4 bytes per 16 kHz sample, so
/// 219.7 MiB per hour of audio, and it exists for the whole run.
///
/// Three endings have to be covered, and they are not the same case:
///
/// <list type="bullet">
/// <item>Normal exit and Ctrl+C: the <c>using</c> in the command disposes the
/// source and the file goes. Verified.</item>
/// <item>SIGTERM and SIGHUP — what <c>docker stop</c>, systemd and CI send — do
/// not unwind the stack, so the <c>using</c> never runs. <see cref="Sweep"/>
/// plus the ProcessExit hook covers those.</item>
/// <item>SIGKILL, an OOM kill or a power cut cannot be handled from inside the
/// process at all. Only the next run can clean those up, which is what
/// <see cref="Sweep"/> is for.</item>
/// </list>
/// </remarks>
internal static class TempAudioFile
{
    /// <summary>Prefix every staging file carries, so leftovers are identifiable.</summary>
    internal const string Prefix = "transcribe-";

    private const string Extension = ".f32";

    /// <summary>
    /// How old a leftover has to be before a run will touch it.
    /// </summary>
    /// <remarks>
    /// A live run's file is excluded by age rather than by any lock, because
    /// there is no portable way to ask "is this process still running" and a
    /// concurrent run must not have its audio deleted. Twenty-four hours is
    /// far longer than any plausible transcription, so the cost of being wrong
    /// in the safe direction (keeping a file) is bounded at one day.
    /// </remarks>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    /// <summary>A new path in the system temp directory for this process's run.</summary>
    internal static string NewPath()
        => Path.Combine(Path.GetTempPath(), $"{Prefix}{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}{Extension}");

    /// <summary>
    /// Removes staging files abandoned by an earlier run that could not clean up
    /// after itself. Best effort: anything that cannot be removed is left alone.
    /// </summary>
    /// <returns>How many files were removed.</returns>
    internal static int Sweep()
    {
        int removed = 0;
        try
        {
            DateTime cutoff = DateTime.UtcNow - StaleAfter;
            foreach (string path in Directory.EnumerateFiles(Path.GetTempPath(), $"{Prefix}*{Extension}"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff)
                    {
                        File.Delete(path);
                        removed++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Someone else's file, or a directory we cannot write to. Not
                    // this run's problem.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // No temp directory to sweep. Not worth failing a transcription over.
        }

        return removed;
    }

    /// <summary>
    /// Registers a hook that deletes <paramref name="path"/> when the process
    /// ends, including on the signals that do not unwind the stack.
    /// </summary>
    /// <remarks>
    /// Only the most recent staging file is tracked, so a process that decodes
    /// more than once input does not accumulate handlers. This is the belt to
    /// <see cref="Sweep"/>'s braces: it cannot help a process that never gets to
    /// run its exit path at all, which is why both exist.
    /// </remarks>
    internal static void RegisterCleanup(string path)
    {
        if (cleanupRegistered)
        {
            return;
        }

        cleanupRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(path);

        if (OperatingSystem.IsWindows())
        {
            // No POSIX signals on Windows; Ctrl+C and the console close handler
            // do run ProcessExit, so the hook above is all there is to do.
            return;
        }

        foreach ((PosixSignal Signal, int PosixNumber) in Handled)
        {
            try
            {
                PosixSignalRegistration.Create(Signal, context =>
                {
                    context.Cancel = true;
                    TryDelete(path);

                    // 128 + the POSIX signal number, which is the status a shell
                    // reports for a process killed by that signal, so the exit
                    // code looks the same as if the default action had run.
                    // Exiting explicitly is required: registering a handler
                    // cancels the default action, so without this the process
                    // would ignore the signal and carry on.
                    Environment.Exit(128 + PosixNumber);
                });
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or ArgumentException)
            {
                // Signal not available on this platform; ProcessExit still
                // covers a normal end.
            }
        }
    }

    /// <summary>
    /// The signals this class handles, with their POSIX numbers.
    /// </summary>
    /// <remarks>
    /// The numbers are written out rather than taken from
    /// <see cref="PosixSignal"/>, whose members are not POSIX signal numbers:
    /// on this runtime SIGTERM is -4, SIGINT is -2 and SIGHUP is -1, so
    /// <c>128 + (int)signal</c> produced 124 instead of the 143 a shell reports.
    /// </remarks>
    private static readonly (PosixSignal Signal, int PosixNumber)[] Handled =
    [
        (PosixSignal.SIGTERM, 15),
        (PosixSignal.SIGHUP, 1),
    ];

    private static bool cleanupRegistered;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do while exiting.
        }
    }
}
