#nullable enable

using System.Text;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// A throwaway directory, so the tests that exercise file handling (input
/// checks, --out, audio loading) do not depend on the repository assets and
/// leave nothing behind.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public string Path { get; }

    public TempWorkspace()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "transcribecppsharp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Combine(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Writes a 16 kHz mono 16-bit WAV holding <paramref name="samples"/>.</summary>
    public string WriteWav(string name, float[] samples)
    {
        string path = Combine(name);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        int dataBytes = samples.Length * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                      // fmt chunk size
        writer.Write((short)1);                // PCM
        writer.Write((short)1);                // mono
        writer.Write(16000);                   // sample rate
        writer.Write(16000 * sizeof(short));   // byte rate
        writer.Write((short)sizeof(short));    // block align
        writer.Write((short)16);               // bits per sample
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (float sample in samples)
        {
            writer.Write((short)Math.Clamp(sample * 32768f, short.MinValue, short.MaxValue));
        }

        return path;
    }

    /// <summary>Writes a file with arbitrary bytes (junk input, truncated WAV…).</summary>
    public string WriteBytes(string name, byte[] content)
    {
        string path = Combine(name);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Writes a text file, named like audio so the WAV reader rejects it.</summary>
    public string WriteText(string name, string content)
    {
        string path = Combine(name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail a test run.
        }
    }
}
