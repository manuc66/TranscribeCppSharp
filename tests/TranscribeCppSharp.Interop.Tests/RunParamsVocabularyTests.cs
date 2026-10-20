using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The vocabulary field of <c>transcribe_run_params</c>: upstream takes an array
/// of UTF-8 strings in priority order, copies them before returning, and ignores
/// them with a warning on a model that does not support the feature.
/// </summary>
public class RunParamsVocabularyTests
{
    [Fact]
    public void TermsAreMarshalledInOrderAsUtf8()
    {
        using var builder = new RunParamsBuilder();

        builder.WithVocabulary(["Alpha", "Bêta", "日本"]);

        RunParams @params = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(3, @params.nVocabulary);
        Assert.NotEqual(IntPtr.Zero, @params.vocabulary);
        Assert.Equal("Alpha", ReadTerm(@params.vocabulary, 0));
        Assert.Equal("Bêta", ReadTerm(@params.vocabulary, 1));
        Assert.Equal("日本", ReadTerm(@params.vocabulary, 2));
    }

    [Fact]
    public void AnEmptyListClearsWhatWasSet()
    {
        using var builder = new RunParamsBuilder();
        builder.WithVocabulary(["Alpha"]);

        builder.WithVocabulary([]);

        RunParams @params = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(0, @params.nVocabulary);
        Assert.Equal(IntPtr.Zero, @params.vocabulary);
    }

    [Fact]
    public void SettingItTwiceReplacesTheFirstList()
    {
        using var builder = new RunParamsBuilder();
        builder.WithVocabulary(["Old"]);

        builder.WithVocabulary(["New", "Second"]);

        RunParams @params = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(2, @params.nVocabulary);
        Assert.Equal("New", ReadTerm(@params.vocabulary, 0));
        Assert.Equal("Second", ReadTerm(@params.vocabulary, 1));
    }

    [Fact]
    public void NullIsRejected()
    {
        using var builder = new RunParamsBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithVocabulary(null!));
    }

    [Fact]
    public void DisposingWithTermsSetDoesNotThrow()
    {
        var builder = new RunParamsBuilder();
        builder.WithVocabulary(["Alpha", "Bêta"]);

        builder.Dispose();
        builder.Dispose();
    }

    /// <summary>
    /// End to end: the strings reach a real run. The tiny model may not implement
    /// the feature — upstream then warns and ignores the list — so this asserts
    /// that the call is accepted and still produces text, which is what the
    /// marshalling has to get right.
    /// </summary>
    [SkippableFact]
    public void ARunAcceptsVocabularyAndStillProducesText()
    {
        Skip.IfNot(
            TestConfig.IsIntegrationTestEnvironment(),
            "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present.");

        Backends.InitDefault();
        using var model = Model.Load(
            TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        float[] pcm = PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        Transcript transcript = session.Run(
            pcm, r => r.WithVocabulary(["TranscribeCppSharp", "GGUF"]));

        Assert.False(string.IsNullOrWhiteSpace(transcript.FullText));
    }

    private static string? ReadTerm(IntPtr array, int index)
        => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(array, index * IntPtr.Size));
}
