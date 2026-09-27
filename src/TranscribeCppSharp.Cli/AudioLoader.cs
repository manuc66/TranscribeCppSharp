// Audio loading: a 16 kHz mono 16-bit WAV is read directly, anything else
// (ogg, mp3, m4a, …) is decoded by ffmpeg, which must be on PATH. The fallback
// exists because the native library only takes raw 16 kHz mono float PCM.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp.Cli;

internal static class AudioLoader
{
    /// <summary>
    /// Opens the input audio as a random-access source of 16 kHz mono float PCM.
    /// Throws <see cref="AudioLoadException"/> (a one-line message, no stack
    /// trace for the user) when the file is neither a readable WAV nor decodable
    /// by ffmpeg.
    /// </summary>
    /// <remarks>
    /// The caller owns the result and must dispose it: an ffmpeg-decoded input
    /// is backed by a temporary file that Dispose removes.
    /// </remarks>
    internal static PcmSource Open(string path)
    {
        try
        {
            WavPcmSource? wav = WavPcmSource.TryOpen(path);
            if (wav is not null)
            {
                return wav;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException
                                      or UnauthorizedAccessException)
        {
            // Not a 16 kHz mono 16-bit WAV (or not readable at all): any other
            // format is decoded with ffmpeg. A truncated file lands here too, and
            // ffmpeg then reports it as undecodable, which is the truth.
        }

        return DecodeToSourceWithFfmpeg(path);
    }

    /// <summary>
    /// Loads the whole input as 16 kHz mono float PCM.
    /// </summary>
    /// <remarks>
    /// Kept for callers that want the entire file. The command itself uses
    /// <see cref="Open"/> and reads one window at a time, because materialising
    /// the file costs 219 MiB for an hour of audio and it is never needed whole.
    /// </remarks>
    internal static float[] Load(string path)
    {
        using PcmSource source = Open(path);
        return source.ReadWindow(0, (int)Math.Min(source.LengthSamples, int.MaxValue));
    }

    private static string ResolveTool(string name)
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => new[]
            {
                Path.Combine(dir, name),
                Path.Combine(dir, name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty))
            });
        return Path.GetFullPath(candidates.FirstOrDefault(File.Exists)
            ?? throw new AudioLoadException(
                $"'{name}' was not found in PATH. Install ffmpeg, or convert the audio first: "
                + "ffmpeg -i input.mp3 -ar 16000 -ac 1 output.wav"));
    }

    /// <summary>
    /// Decodes <paramref name="path"/> with ffmpeg into 16 kHz mono f32 PCM, and
    /// returns the whole thing.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so the tests can drive it directly: the
    /// public <see cref="Load"/> short-circuits a 16 kHz mono WAV to the direct
    /// reader, which is the case these tests need to compare against, and that
    /// combination is otherwise unreachable from outside the class. The command
    /// itself uses the streaming source instead.
    /// </remarks>
    internal static float[] DecodeWithFfmpeg(string path)
    {
        using FfmpegPcmSource source = DecodeToSourceWithFfmpeg(path);
        return source.ReadWindow(0, (int)Math.Min(source.LengthSamples, int.MaxValue));
    }

    /// <summary>
    /// Runs ffmpeg once, writing raw f32le to a temporary file, and returns a
    /// source that reads windows out of it. The file lives as long as the source.
    /// </summary>
    private static FfmpegPcmSource DecodeToSourceWithFfmpeg(string path)
    {
        // ffmpeg writes raw f32le to a temporary file rather than to stdout.
        // A pipe does not carry its length, so reading one means buffering the
        // whole stream before its size is known; this code did that through a
        // MemoryStream, which held up to three full-size copies at once (the
        // growth buffer, its ToArray() copy, and the float[] being built).
        // Measured peak RSS for 30 minutes of audio: 610 MiB that way, against
        // 299 MiB for the direct WAV reader. A file gives the length up front and
        // can be read in windows, so the decoded audio never has to be resident.
        string temp = Path.Combine(Path.GetTempPath(), $"transcribe-{Guid.NewGuid():N}.f32");
        try
        {
            // -nostdin: ffmpeg reads stdin by default and stdin is inherited
            // here, so in an interactive terminal it can consume the user's
            // keystrokes. It is also the documented way to stop a child blocking
            // on an input stream nobody is feeding.
            //
            // Nothing is redirected. The output goes to the file, and a
            // RedirectStandardOutput here would create a pipe that nothing
            // reads, so a single byte from ffmpeg would fill it and block the
            // process forever. stderr is left inherited for the same reason: the
            // `-v error` output goes to the console, where a user can act on it,
            // and capturing it would need a reader to avoid the same hazard.
            var psi = new ProcessStartInfo
            {
                FileName = ResolveTool("ffmpeg"),
                Arguments = $"-nostdin -v error -y -i \"{path}\" -ar 16000 -ac 1 -f f32le \"{temp}\"",
                UseShellExecute = false,
            };

            Process proc;
            try
            {
                proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            }
            catch (Exception ex) when (ex is not AudioLoadException)
            {
                throw new AudioLoadException($"Could not start ffmpeg: {ex.Message}", ex);
            }

            using (proc)
            {
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    throw new AudioLoadException(
                        $"could not decode {path}: it is not a 16 kHz mono 16-bit WAV and ffmpeg could not decode it either. "
                        + "Convert it first (ffmpeg -i input -ar 16000 -ac 1 output.wav) or install ffmpeg.");
                }
            }

            return OpenF32File(temp, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp directory that cannot be written is a real possibility
            // (read-only /tmp, a full disk). Say so instead of surfacing a raw
            // .NET exception as a stack trace.
            TryDelete(temp);
            throw new AudioLoadException(
                $"could not decode {path}: ffmpeg ran but its output could not be staged in the temporary directory "
                + $"({temp}): {ex.Message}", ex);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Wraps the decoded f32 file in a source, checking it is a whole number of
    /// samples and that its length is one this tool can address.
    /// </summary>
    private static FfmpegPcmSource OpenF32File(string temp, string source)
    {
        var info = new FileInfo(temp);
        if (!info.Exists)
        {
            TryDelete(temp);
            throw new AudioLoadException($"ffmpeg reported success but produced no output for {source}.");
        }

        long length = info.Length;
        if (length % sizeof(float) != 0)
        {
            TryDelete(temp);
            throw new AudioLoadException($"ffmpeg returned a truncated PCM stream for {source}.");
        }

        // Cap at int.MaxValue: a window is read into a float[] indexed by int, so
        // the sample count has to fit one. A window is far below this in practice;
        // the guard is here so the failure is one clear line rather than a wrap.
        if (length / sizeof(float) > int.MaxValue)
        {
            TryDelete(temp);
            throw new AudioLoadException(
                $"{source} decodes to {length / sizeof(float)} samples, which is more than this tool can index.");
        }

        var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: false);
        return new FfmpegPcmSource(temp, stream, length / sizeof(float));
    }

    private static void TryDelete(string temp)
    {
        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file must not fail the run.
        }
    }
}

/// <summary>
/// The input audio could not be read. Carries a message meant for the user, so
/// the command reports it instead of letting a .NET stack trace escape.
/// </summary>
/// <remarks>
/// Public because an exception type is part of a contract: a caller that wraps
/// the loader has to be able to catch this one specifically rather than
/// <see cref="Exception"/>. It was internal, on the reasoning that the CLI ships
/// as a .NET tool and nothing outside the assembly ever sees it — true of the
/// process, but the project also references this assembly from its tests and the
/// rule exists for a reason. The type is documented, so the public surface costs
/// nothing here.
/// </remarks>
public sealed class AudioLoadException : Exception
{
    /// <summary>Creates the exception with a message meant for the user.</summary>
    /// <param name="message">One line, no stack trace intended.</param>
    public AudioLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the underlying cause attached.</summary>
    /// <param name="message">One line, no stack trace intended.</param>
    /// <param name="innerException">What actually failed.</param>
    public AudioLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
