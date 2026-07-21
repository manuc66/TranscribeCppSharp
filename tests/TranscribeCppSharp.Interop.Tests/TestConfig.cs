#nullable enable

namespace TranscribeCppSharp.Interop.Tests;

public static class TestConfig
{
    public static string ModelPath => "./test-models/ggml-tiny.bin";
    public static string AudioPath => "./test-audio/test.wav";

    public static bool IsIntegrationTestEnvironment()
    {
        return System.IO.File.Exists(ModelPath) && System.IO.File.Exists(AudioPath);
    }
}
