using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the owner-only temporary file every staged audio path uses.
/// </summary>
/// <remarks>
/// The mode matters as much as the deletion: the file holds the audio being
/// transcribed, and on a shared host the default 0644 that <c>File.Create</c>
/// produces makes it readable by every local user for the whole run.
/// </remarks>
public class PrivateTempFileTests
{
    [Fact]
    public void Create_IsOwnerOnlyOnUnix()
    {
        using var temp = new TempWorkspace();
        string path = temp.Combine("staged.f32");

        using (PrivateTempFile.Create(path))
        {
        }

        if (OperatingSystem.IsWindows())
        {
            // The inherited temp-directory ACL is what limits it there; there is
            // no mode to assert.
            Assert.True(File.Exists(path));
            return;
        }

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(path));
    }

    [Fact]
    public void TruncatingTheReservedFileKeepsTheMode()
    {
        // ffmpeg is started with "-y", which opens the path we reserved and
        // truncates it in place rather than creating a new file. That is what
        // makes reserving it beforehand enough to keep the decoded PCM private.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempWorkspace();
        string path = temp.Combine("staged.f32");

        using (PrivateTempFile.Create(path))
        {
        }

        using (new FileStream(path, FileMode.Create, FileAccess.Write))
        {
        }

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(path));
    }
}
