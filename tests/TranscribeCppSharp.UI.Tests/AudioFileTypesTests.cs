using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Browse dialog and the drop handler share one extension list, and it is
/// what decides whether a file can be opened at all. These tests pin the two
/// things the list has to get right: the containers ffmpeg decodes are accepted,
/// and everything else is not, so a drop shows a copy cursor only over a file
/// that will actually be read.
/// </summary>
public sealed class AudioFileTypesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "tcsharp-audio-types-" + Guid.NewGuid().ToString("N"));

    public AudioFileTypesTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory must not fail a test run.
        }
    }

    /// <summary>Creates an empty file with the given extension and returns its path.</summary>
    private string FileNamed(string extension)
    {
        string path = Path.Combine(_directory, "sample" + extension);
        File.WriteAllBytes(path, []);
        return path;
    }

    [Theory]
    [InlineData(".wav")]
    [InlineData(".mp3")]
    [InlineData(".flac")]
    [InlineData(".ogg")]
    [InlineData(".m4a")]
    [InlineData(".aac")]
    [InlineData(".opus")]
    [InlineData(".wma")]
    [InlineData(".mp4")]
    [InlineData(".m4v")]
    [InlineData(".mkv")]
    [InlineData(".mov")]
    [InlineData(".avi")]
    [InlineData(".webm")]
    [InlineData(".3gp")]
    public void AnAudioOrVideoFileCanBeDropped(string extension)
    {
        Assert.True(TranscriptionViewModel.CanAcceptDroppedFile(FileNamed(extension)));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".pdf")]
    [InlineData(".gguf")]
    [InlineData("")]
    public void AnythingElseCannot(string extension)
    {
        Assert.False(TranscriptionViewModel.CanAcceptDroppedFile(FileNamed(extension)));
    }

    [Fact]
    public void AMissingFileCannotBeDropped()
    {
        Assert.False(TranscriptionViewModel.CanAcceptDroppedFile(
            Path.Combine(_directory, "absent.mp4")));
    }

    [Fact]
    public void NothingCannotBeDropped()
    {
        Assert.False(TranscriptionViewModel.CanAcceptDroppedFile(null));
        Assert.False(TranscriptionViewModel.CanAcceptDroppedFile("   "));
    }
}
