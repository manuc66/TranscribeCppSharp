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
    /// <see cref="InvalidDataException"/> when the file is neither a readable
    /// WAV nor something ffmpeg can decode.
    /// </summary>
    internal static float[] Load(string path)
    {
        try
        {
            return PcmExtensions.ReadWavToPcm(path);
        }
        catch (InvalidDataException)
        {
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
            ?? throw new InvalidOperationException($"'{name}' not found in PATH."));
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

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        using var stdout = proc.StandardOutput.BaseStream;
        using var buffer = new MemoryStream();
        stdout.CopyTo(buffer);
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            throw new InvalidDataException(
                "ffmpeg could not decode the audio. Install ffmpeg, or convert to a 16 kHz mono 16-bit WAV first.");
        }

        var bytes = buffer.ToArray();
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException("ffmpeg returned a truncated PCM stream.");
        }

        var pcm = new float[bytes.Length / sizeof(float)];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        }

        return pcm;
    }
}
