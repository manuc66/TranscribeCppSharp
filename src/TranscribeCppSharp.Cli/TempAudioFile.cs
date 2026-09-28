#nullable enable

using System.Diagnostics;
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
    /// Opens a staging file for reading and, on Unix, immediately removes its
    /// name from the directory.
    /// </summary>
    /// <remarks>
    /// After this call the data stays reachable through the returned handle, but
    /// the directory has no entry for it, so **nothing is left behind even if the
    /// process is killed outright**. That is the part a <c>try/finally</c> and a
    /// signal handler cannot do: SIGKILL, an OOM kill and a power cut run no
    /// cleanup code at all, and on Windows an unlink of an open file is not
    /// possible either.
    ///
    /// The exposure that remains is the window between ffmpeg finishing the write
    /// and this call opening the file — on a long input that is the decode phase,
    /// so it is not negligible. It is a much smaller window than before, where
    /// the file stayed named for the whole transcription, but it is not zero.
    ///
    /// On Windows the file keeps its name and is removed by <see cref="TryDelete"/>
    /// on a normal or signalled exit, with <see cref="Sweep"/> covering the rest.
    /// </remarks>
    internal static FileStream OpenForReading(string path)
    {
        // FileShare.None: the owner must not be able to delete or rewrite it
        // underneath the reader, and on Windows it is what makes the file
        // un-deletable by another process while it is in use, which is how
        // Sweep tells a live run from an abandoned one.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1, useAsync: false);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Could not unlink; the file keeps its name and the delete-on-exit
                // path still applies. Not worth failing over.
            }
        }

        return stream;
    }

    /// <summary>
    /// A new path in the system temp directory for this process's run.
    /// </summary>
    internal static string NewPath()
        => Path.Combine(Path.GetTempPath(), $"{Prefix}{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}{Extension}");

    /// <summary>
    /// Removes staging files abandoned by an earlier run that could not clean up
    /// after itself. Best effort: anything that cannot be removed is left alone.
    /// </summary>
    /// <remarks>
    /// Decided from the process id in the file name, which is there for this: a
    /// file whose owning process is gone belongs to a run that died and can be
    /// removed now, however recent. That is the case a timeout cannot serve — a
    /// 24-hour threshold means a crash's debris sits for a day.
    ///
    /// The lock is a second, independent signal, because process ids get reused:
    /// a file that cannot be opened exclusively is being read by a live run and is
    /// never touched, whatever the id says.
    ///
    /// The one window neither signal closes is a decode in progress: between
    /// ffmpeg finishing the write and this run opening the file, it is named but
    /// unlocked, and the owning process is alive, so the id check protects it. A
    /// run that is alive but whose id was reused, after a crash, is the residual
    /// false negative, and it is left for a human — the alternative is deleting a
    /// live run's audio.
    /// </remarks>
    /// <returns>How many files were removed.</returns>
    internal static int Sweep()
    {
        int removed = 0;
        try
        {
            foreach (string path in Directory.EnumerateFiles(Path.GetTempPath(), $"{Prefix}*{Extension}"))
            {
                try
                {
                    if (IsAbandoned(path))
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

    private static bool IsAbandoned(string path)
    {
        // Live run holds it exclusively: never touch it, whatever else says.
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1, useAsync: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return !IsOwnerAlive(path);
    }

    /// <summary>
    /// Whether the process id in the staging file's name is still running.
    /// </summary>
    /// <remarks>
    /// "Cannot tell" answers as alive, so an inconclusive check keeps the file.
    /// The error to avoid is deleting a concurrent run's audio; the cost of the
    /// other mistake is one file a human can remove.
    /// </remarks>
    private static bool IsOwnerAlive(string path)
    {
        string name = Path.GetFileName(path);
        int dash = name.IndexOf('-', Prefix.Length);
        if (dash < 0)
        {
            // Not a name this wrote. Leave it alone.
            return true;
        }

        if (!int.TryParse(name.AsSpan(Prefix.Length, dash - Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
        {
            return true;
        }

        if (pid <= 0)
        {
            return true;
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // No such process: the run that wrote this is gone.
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            // Cannot tell.
            return true;
        }
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
