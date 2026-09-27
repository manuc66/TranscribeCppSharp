#nullable enable

using System;
using System.IO;
using System.Linq;

namespace TranscribeCppSharp.Interop.Tests;

public static class TestConfig
{
    private static readonly string RootPath = FindRoot(AppContext.BaseDirectory);

    public static string RepoRoot => RootPath;
    public static string ModelPath => Path.Combine(RootPath, "test-models/ggml-tiny.bin");
    public static string AudioPath => Path.Combine(RootPath, "test-audio/jfk.wav");

    public static bool IsIntegrationTestEnvironment()
    {
        return File.Exists(ModelPath) && File.Exists(AudioPath);
    }

    /// <summary>
    /// Whether an <c>ffmpeg</c> executable is on PATH, the same way
    /// <c>AudioLoader.ResolveTool</c> looks for it.
    /// </summary>
    /// <remarks>
    /// Not every CI runner image has ffmpeg, and the CLI reports a different
    /// (equally valid) message when it is missing than when it runs and fails to
    /// decode. Tests that reach the decode path must branch on this rather than
    /// assert one of the two messages unconditionally: two of them did, and CI
    /// went red on a runner without ffmpeg.
    /// </remarks>
    public static bool HasFfmpeg() => ResolveFfmpeg() is not null;

    private static string? ResolveFfmpeg()
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);
    }

    private static string FindRoot(string startDir)
    {
        var current = new DirectoryInfo(startDir);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TranscribeCppSharp.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        return startDir;
    }
}
