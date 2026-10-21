using System.Diagnostics;
using System.Globalization;

namespace TranscribeCppSharp.UI.Services;

/// <summary>Plays one transcript segment's audio.</summary>
public interface ISegmentPlayer
{
    /// <summary>
    /// Starts playing <paramref name="audioPath"/> from <paramref name="start"/> to
    /// <paramref name="endTime"/>. Returns false with a message when it cannot.
    /// </summary>
    bool TryPlay(string audioPath, TimeSpan start, TimeSpan endTime, out string? failure);
}

/// <summary>
/// Plays a segment with <c>ffplay</c>, the player that ships with ffmpeg.
/// </summary>
/// <remarks>
/// The app already depends on ffmpeg for non-WAV input and the microphone, but
/// <c>ffplay</c> is a separate executable: on a machine with only the encoder it
/// is absent, and the caller gets a sentence rather than a silent no-op. Starting
/// a second segment stops the first, so two excerpts never play over each other.
/// </remarks>
public sealed class FfplaySegmentPlayer : ISegmentPlayer
{
    private Process? _process;

    /// <inheritdoc/>
    public bool TryPlay(string audioPath, TimeSpan start, TimeSpan endTime, out string? failure)
    {
        failure = null;

        if (!File.Exists(audioPath))
        {
            failure = $"the audio file is not there: {audioPath}";
            return false;
        }

        string? ffplay = ResolveFfplay();
        if (ffplay is null)
        {
            failure = "ffplay was not found on PATH. It ships with ffmpeg; install ffmpeg to play excerpts.";
            return false;
        }

        Stop();

        var info = new ProcessStartInfo(ffplay)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in BuildArguments(audioPath, start, endTime))
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            _process = Process.Start(info);
            return _process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            failure = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// The ffplay arguments for one excerpt. A list rather than a command line, so a
    /// path with spaces or quotes needs no escaping.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(string audioPath, TimeSpan start, TimeSpan endTime)
    {
        double from = Math.Max(0, start.TotalSeconds);
        double duration = Math.Max(0.1, (endTime - start).TotalSeconds);
        return
        [
            "-nodisp",
            "-autoexit",
            "-loglevel", "error",
            "-ss", from.ToString("0.###", CultureInfo.InvariantCulture),
            "-t", duration.ToString("0.###", CultureInfo.InvariantCulture),
            audioPath,
        ];
    }

    private void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                      or NotSupportedException)
        {
            // The process ended between the check and the kill: nothing to stop.
        }

        _process?.Dispose();
        _process = null;
    }

    /// <summary>The ffplay executable on PATH, or null when it is not installed.</summary>
    private static string? ResolveFfplay()
    {
        string executable = OperatingSystem.IsWindows() ? "ffplay.exe" : "ffplay";
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
