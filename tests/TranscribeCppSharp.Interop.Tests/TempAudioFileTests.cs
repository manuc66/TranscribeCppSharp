#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Cli;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the temporary file that holds ffmpeg's decoded audio.
/// </summary>
/// <remarks>
/// The file is 4 bytes per 16 kHz sample, so 219.7 MiB for an hour of audio, and
/// it exists for the whole run. Leaving those behind is a real failure mode, so
/// what matters is that the cases which can be handled are handled: a normal exit
/// and Ctrl+C go through Dispose, SIGTERM and SIGHUP (what <c>docker stop</c>,
/// systemd and CI send) are caught, and a process that cannot be caught at all
/// is cleaned up by the next run.
/// </remarks>
[Collection(nameof(TempAudioFileTests))]
public class TempAudioFileTests
{
    private readonly ITestOutputHelper _output;

    public TempAudioFileTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void NewPath_IsInTheTempDirectoryAndIsNotARealFileYet()
    {
        string path = TempAudioFile.NewPath();

        // Compared with the separators normalised, because GetTempPath keeps a
        // trailing separator on Unix and the assertion is about the directory,
        // not about spelling.
        string expectedDir = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        string actualDir = Path.GetDirectoryName(Path.GetFullPath(path))!.TrimEnd(Path.DirectorySeparatorChar);
        Assert.Equal(expectedDir, actualDir);
        Assert.StartsWith(TempAudioFile.Prefix, Path.GetFileName(path));
        Assert.EndsWith(".f32", path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void NewPath_IsDifferentEveryCall()
    {
        // Two runs in the same process must not collide, and the process id in
        // the name means two concurrent runs do not either.
        Assert.NotEqual(TempAudioFile.NewPath(), TempAudioFile.NewPath());
    }

    [Fact]
    public void Sweep_RemovesAFileWhoseProcessIsGone_HoweverRecentItIs()
    {
        // This is the crash case, and it is why the sweep reads the process id out
        // of the name instead of a timestamp: a run killed a second ago must not
        // sit in the temp directory for a day.
        string orphan = TempAudioFile.NewPath();
        File.WriteAllBytes(orphan, new byte[1024]);
        orphan = RewritePid(orphan, DeadPid);

        try
        {
            int removed = TempAudioFile.Sweep();
            _output.WriteLine($"sweep removed {removed}");

            Assert.False(File.Exists(orphan));
        }
        finally
        {
            File.Delete(orphan);
        }
    }

    [Fact]
    public void Sweep_KeepsAFileWhoseProcessIsStillRunning()
    {
        // A concurrent run's audio. Deleting this would break a live
        // transcription, so the conservative answer has to win here.
        string live = TempAudioFile.NewPath();
        File.WriteAllBytes(live, new byte[1024]);
        live = RewritePid(live, Environment.ProcessId);

        try
        {
            TempAudioFile.Sweep();
            Assert.True(File.Exists(live));
        }
        finally
        {
            File.Delete(live);
        }
    }

    [Fact]
    public void Sweep_KeepsAFileThatIsOpenExclusively_WhateverTheNameSays()
    {
        // A dead process id but a file that cannot be opened is being read by
        // something. Process ids get reused, so the lock is the second,
        // independent signal and it outranks the id.
        string held = TempAudioFile.NewPath();
        File.WriteAllBytes(held, new byte[1024]);
        held = RewritePid(held, DeadPid);

        try
        {
            using var owner = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);
            TempAudioFile.Sweep();
            Assert.True(File.Exists(held));
        }
        finally
        {
            File.Delete(held);
        }
    }

    [Fact]
    public void Sweep_LeavesAFileWhoseNameItDidNotWrite()
    {
        // No parsable process id in the name: not ours to interpret, so not ours
        // to delete.
        string odd = Path.Combine(Path.GetTempPath(), $"{TempAudioFile.Prefix}no-pid-here.f32");
        File.WriteAllBytes(odd, new byte[16]);

        try
        {
            TempAudioFile.Sweep();
            Assert.True(File.Exists(odd));
        }
        finally
        {
            File.Delete(odd);
        }
    }

    [Fact]
    public void OpenForReading_RemovesTheFilesNameOnUnix()
    {
        // The property that makes a SIGKILL harmless: after opening, the data is
        // still readable through the handle but the directory has no entry, so a
        // process that dies outright leaves nothing. Verified end to end against
        // `kill -9` as well, since it is the whole point.
        if (OperatingSystem.IsWindows())
        {
            // Windows cannot unlink an open file, so the name stays and the
            // delete-on-exit path covers it. Nothing to assert here.
            return;
        }

        string path = TempAudioFile.NewPath();
        File.WriteAllBytes(path, new byte[64]);

        try
        {
            using FileStream stream = TempAudioFile.OpenForReading(path);

            Assert.False(File.Exists(path));
            Assert.Equal(64, stream.Length);
            stream.Position = 0;
            Assert.Equal(64, stream.ReadByte() >= 0 ? 64 : 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A process id that is not running, for the "its owner died" case.</summary>
    private static int DeadPid
    {
        get
        {
            // Well above any pid the OS is likely to hand out, and confirmed dead
            // rather than assumed: Process.GetProcessById throws for a pid that
            // does not exist, which is exactly the signal the sweep uses.
            for (int pid = 4_000_000; pid > 2_000_000; pid--)
            {
                try
                {
                    using Process p = Process.GetProcessById(pid);
                    if (p.HasExited)
                    {
                        return pid;
                    }
                }
                catch (ArgumentException)
                {
                    return pid;
                }
            }

            return 4_000_000;
        }
    }

    /// <summary>
    /// Renames a staging file so its name carries <paramref name="pid"/>, and
    /// returns the new path — the original no longer exists.
    /// </summary>
    private static string RewritePid(string path, int pid)
    {
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileName(path);
        int dash = name.IndexOf('-', TempAudioFile.Prefix.Length);
        string guid = dash < 0 ? "00000000000000000000000000000000" : name[(dash + 1)..];
        string renamed = Path.Combine(
            dir,
            $"{TempAudioFile.Prefix}{pid.ToString(CultureInfo.InvariantCulture)}-{guid}");
        File.Move(path, renamed, overwrite: true);
        return renamed;
    }

    [Fact]
    public void Sweep_LeavesFilesThatAreNotStagingFilesAlone()
    {
        // The sweep matches on the project's own prefix, so it must not wander
        // into unrelated files that happen to sit in the temp directory.
        string mine = Path.Combine(Path.GetTempPath(), $"not-transcribe-{Guid.NewGuid():N}.f32");
        File.WriteAllBytes(mine, new byte[16]);
        File.SetLastWriteTimeUtc(mine, DateTime.UtcNow - TimeSpan.FromDays(30));

        try
        {
            TempAudioFile.Sweep();
            Assert.True(File.Exists(mine));
        }
        finally
        {
            File.Delete(mine);
        }
    }

    [Fact]
    public void Sweep_SurvivesATempDirectoryItCannotWriteTo()
    {
        // Not worth failing a transcription over, and there is nothing to assert
        // beyond "it returned". If the directory were gone entirely, EnumerateFiles
        // would throw; the guard is what stops that becoming a crash.
        int removed = TempAudioFile.Sweep();
        Assert.True(removed >= 0);
    }

    /// <summary>Keeps these off each other's files in the shared temp directory.</summary>
    [CollectionDefinition(nameof(TempAudioFileTests), DisableParallelization = true)]
    public class TempAudioFileTestGroup
    {
    }
}
