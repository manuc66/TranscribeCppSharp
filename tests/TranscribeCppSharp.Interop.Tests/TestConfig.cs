#nullable enable

using System;
using System.IO;

namespace TranscribeCppSharp.Interop.Tests;

public static class TestConfig
{
    private static readonly string RootPath = FindRoot(AppContext.BaseDirectory);

    public static string ModelPath => Path.Combine(RootPath, "test-models/ggml-tiny.bin");
    public static string AudioPath => Path.Combine(RootPath, "test-audio/jfk.wav");

    public static bool IsIntegrationTestEnvironment()
    {
        return File.Exists(ModelPath) && File.Exists(AudioPath);
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
