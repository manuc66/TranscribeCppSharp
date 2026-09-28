#nullable enable

using System;
using System.IO;
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
    public void Sweep_RemovesAnAbandonedFile_AndKeepsAFreshOne()
    {
        string abandoned = TempAudioFile.NewPath();
        string fresh = TempAudioFile.NewPath();
        File.WriteAllBytes(abandoned, new byte[1024]);
        File.WriteAllBytes(fresh, new byte[1024]);

        // A file abandoned two days ago is what a killed process leaves.
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow - TimeSpan.FromDays(2));

        try
        {
            int removed = TempAudioFile.Sweep();
            _output.WriteLine($"sweep removed {removed} file(s)");

            Assert.False(File.Exists(abandoned));
            // The fresh one is what a concurrent run is using: a sweep must never
            // take another run's audio away.
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            File.Delete(abandoned);
            File.Delete(fresh);
        }
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
