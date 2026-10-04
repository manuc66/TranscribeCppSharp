// Audio loading: a 16 kHz mono 16-bit WAV is read directly, anything else
// (ogg, mp3, m4a, …) is decoded by ffmpeg, which must be on PATH. The fallback
// exists because the native library only takes raw 16 kHz mono float PCM.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp.Audio;

/// <summary>
/// Opens the input audio as a random-access source of 16 kHz mono float PCM.
/// Throws <see cref="AudioLoadException"/> when the file is neither a readable WAV nor decodable
/// by ffmpeg.
/// </summary>
public static class AudioLoader
{
    /// <summary>
    /// Opens the input audio as a random-access source of 16 kHz mono float PCM.
    /// </summary>
    public static PcmSource Open(string path)
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
            // format is decoded with ffmpeg.
        }

        return DecodeToSourceWithFfmpeg(path);
    }

    /// <summary>
    /// Loads the whole input as 16 kHz mono float PCM.
    /// </summary>
    public static float[] Load(string path)
    {
        using PcmSource source = Open(path);
        return source.ReadWindow(0, (int)Math.Min(source.LengthSamples, int.MaxValue));
    }

    /// <summary>
    /// Decodes <paramref name="path"/> with ffmpeg into 16 kHz mono f32 PCM,
    /// bypassing the direct WAV reader.
    /// </summary>
    /// <remarks>
    /// Public rather than private so tests can drive the decode path directly:
    /// <see cref="Load"/> short-circuits a 16 kHz mono WAV to the direct reader,
    /// which is the comparison these tests need and is otherwise unreachable
    /// from outside this class.
    /// </remarks>
    public static float[] DecodeWithFfmpeg(string path)
    {
        using FfmpegPcmSource source = DecodeToSourceWithFfmpeg(path);
        return source.ReadWindow(0, (int)Math.Min(source.LengthSamples, int.MaxValue));
    }

    private static string ResolveTool(string name)
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => new[] { Path.Combine(dir, name), Path.Combine(dir, name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)), });
        return Path.GetFullPath(candidates.FirstOrDefault(File.Exists)
            ?? throw new AudioLoadException(
                $"'{name}' was not found in PATH. Install ffmpeg, or convert the audio first: "
                + "ffmpeg -i input.mp3 -ar 16000 -ac 1 output.wav"));
    }

    private static FfmpegPcmSource DecodeToSourceWithFfmpeg(string path)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"transcribe_{Guid.NewGuid():N}.f32");

        try
        {
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
            TryDelete(temp);
            throw new AudioLoadException($"could not decode {path}: ffmpeg ran but its output could not be staged in the temporary directory ({temp}): {ex.Message}", ex);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

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

        if (length / sizeof(float) > int.MaxValue)
        {
            TryDelete(temp);
            throw new AudioLoadException(
                $"{source} decodes to {length / sizeof(float)} samples, which is more than this tool can index.");
        }

        var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
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
