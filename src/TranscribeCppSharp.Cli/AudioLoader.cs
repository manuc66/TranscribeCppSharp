// Audio loading: a 16 kHz mono 16-bit WAV is read directly, anything else
// (ogg, mp3, m4a, …) is decoded by ffmpeg, which must be on PATH. The fallback
// exists because the native library only takes raw 16 kHz mono float PCM.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp.Cli;

internal static class AudioLoader
{
    /// <summary>
    /// Loads the input audio as 16 kHz mono float PCM. Throws
    /// <see cref="AudioLoadException"/> (a one-line message, no stack trace for
    /// the user) when the file is neither a readable WAV nor decodable by
    /// ffmpeg.
    /// </summary>
    internal static float[] Load(string path)
    {
        try
        {
            return PcmExtensions.ReadWavToPcm(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or UnauthorizedAccessException)
        {
            // Not a 16 kHz mono 16-bit WAV (or not readable at all): any other
            // format is decoded with ffmpeg. A truncated file lands here too, and
            // ffmpeg then reports it as undecodable, which is the truth.
            return DecodeWithFfmpeg(path);
        }
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
    /// Decodes <paramref name="path"/> with ffmpeg into 16 kHz mono f32 PCM.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so the tests can drive it directly: the
    /// public <see cref="Load"/> short-circuits a 16 kHz mono WAV to the direct
    /// reader, which is the case these tests need to compare against, and that
    /// combination is otherwise unreachable from outside the class.
    /// </remarks>
    internal static float[] DecodeWithFfmpeg(string path)
    {
        // ffmpeg writes raw f32le to a temporary file rather than to stdout.
        // A pipe does not carry its length, so reading one means buffering the
        // whole stream before its size is known; this code did that through a
        // MemoryStream, which held up to three full-size copies at once (the
        // growth buffer, its ToArray() copy, and the float[] being built).
        // Measured peak RSS for 30 minutes of audio: 610 MiB that way, against
        // 299 MiB for the direct WAV reader. With a file the length is known up
        // front, so the float[] is the only large allocation.
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

            return ReadF32File(temp, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp directory that cannot be written is a real possibility
            // (read-only /tmp, a full disk). Say so instead of surfacing a raw
            // .NET exception as a stack trace.
            throw new AudioLoadException(
                $"could not decode {path}: ffmpeg ran but its output could not be staged in the temporary directory "
                + $"({temp}): {ex.Message}", ex);
        }
        finally
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
    /// Reads a raw little-endian f32 file into a float[]. The file's length is
    /// known before allocating, so the result is the only large allocation.
    /// </summary>
    private static float[] ReadF32File(string temp, string source)
    {
        var info = new FileInfo(temp);
        if (!info.Exists)
        {
            throw new AudioLoadException($"ffmpeg reported success but produced no output for {source}.");
        }

        long length = info.Length;
        if (length % sizeof(float) != 0)
        {
            throw new AudioLoadException($"ffmpeg returned a truncated PCM stream for {source}.");
        }

        // Cap at int.MaxValue: a float[] is indexed with int, and the length has
        // to fit the array anyway.
        if (length > int.MaxValue)
        {
            throw new AudioLoadException(
                $"{source} decodes to {length} bytes of PCM, which is more than this tool can hold in one array.");
        }

        var pcm = new float[length / sizeof(float)];
        using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: false))
        {
            // Read straight into the float[] through its byte view: no second
            // buffer, and the endianness is ffmpeg's documented f32le.
            Span<float> samples = pcm;
            stream.ReadExactly(MemoryMarshal.AsBytes(samples));
        }

        return pcm;
    }
}

/// <summary>
/// The input audio could not be read. Carries a message meant for the user, so
/// the command reports it instead of letting a .NET stack trace escape.
/// </summary>
// NOSONAR csharpsquid:S3871 — this type is deliberately internal. The CLI ships
// as a .NET tool, and a public exception here would put an implementation type
// of the command into the package's public API for no consumer to catch: the
// process reports the message on stderr and exits 1, and nothing outside the
// assembly ever sees this. The rest of the CLI is internal for the same reason.
internal sealed class AudioLoadException : Exception
{
    internal AudioLoadException(string message)
        : base(message)
    {
    }

    internal AudioLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
