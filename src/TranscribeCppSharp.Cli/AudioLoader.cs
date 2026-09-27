// Audio loading: a 16 kHz mono 16-bit WAV is read directly, anything else
// (ogg, mp3, m4a, …) is decoded by ffmpeg, which must be on PATH. The fallback
// exists because the native library only takes raw 16 kHz mono float PCM.

using System.Buffers.Binary;
using System.Diagnostics;

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

    private static float[] DecodeWithFfmpeg(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveTool("ffmpeg"),
            Arguments = $"-v error -i \"{path}\" -ar 16000 -ac 1 -f f32le -",
            RedirectStandardOutput = true,
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
            using var stdout = proc.StandardOutput.BaseStream;
            using var buffer = new MemoryStream();
            stdout.CopyTo(buffer);
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                throw new AudioLoadException(
                    $"could not decode {path}: it is not a 16 kHz mono 16-bit WAV and ffmpeg could not decode it either. "
                    + "Convert it first (ffmpeg -i input -ar 16000 -ac 1 output.wav) or install ffmpeg.");
            }

            var bytes = buffer.ToArray();
            if (bytes.Length % sizeof(float) != 0)
            {
                throw new AudioLoadException($"ffmpeg returned a truncated PCM stream for {path}.");
            }

            var pcm = new float[bytes.Length / sizeof(float)];
            for (int i = 0; i < pcm.Length; i++)
            {
                pcm[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
            }

            return pcm;
        }
    }
}

/// <summary>
/// The input audio could not be read. Carries a message meant for the user, so
/// the command reports it instead of letting a .NET stack trace escape.
/// </summary>
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
