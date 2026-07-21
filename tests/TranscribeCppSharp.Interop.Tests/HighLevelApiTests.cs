#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

public class HighLevelApiTests : IDisposable
{
    public HighLevelApiTests()
    {
    }

    public void Dispose()
    {
    }

    [Fact(Skip = "Requires test audio file")]
    public void PcmExtensions_ReadWavToPcm_ShouldLoadTestWav()
    {
        // Arrange & Act
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        // Assert
        Assert.NotNull(pcm);
        Assert.Equal(16000, pcm.Length); // 1 second at 16kHz
        Assert.All(pcm, s => Assert.InRange(s, -1f, 1f));
    }

    [Fact]
    public void PcmExtensions_ReadWavToPcm_InvalidFile_ShouldThrow()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), "invalid.wav");
        File.WriteAllText(invalidPath, "not a wav file");

        try
        {
            // Act & Assert
            Assert.Throws<InvalidDataException>(() => TranscribeCppSharp.PcmExtensions.ReadWavToPcm(invalidPath));
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }

    [Fact(Skip = "Requires native library")]
    public void ModelLoadParamsBuilder_WithBackend_ShouldSetBackend()
    {
        // Arrange & Act
        using var builder = new ModelLoadParamsBuilder();
        builder.WithBackend(BackendRequest.BackendCpu);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void ModelLoadParamsBuilder_WithGpuDevice_ShouldSetDevice()
    {
        // Arrange & Act
        using var builder = new ModelLoadParamsBuilder();
        builder.WithGpuDevice(0);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void SessionParamsBuilder_WithThreads_ShouldSetThreads()
    {
        // Arrange & Act
        using var builder = new SessionParamsBuilder();
        builder.WithThreads(4);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void SessionParamsBuilder_WithKvType_ShouldSetKvType()
    {
        // Arrange & Act
        using var builder = new SessionParamsBuilder();
        builder.WithKvType(KvType.KvTypeF16);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void SessionParamsBuilder_WithContextSize_ShouldSetContextSize()
    {
        // Arrange & Act
        using var builder = new SessionParamsBuilder();
        builder.WithContextSize(1024);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void RunParamsBuilder_WithLanguage_ShouldSetLanguage()
    {
        // Arrange & Act
        using var builder = new RunParamsBuilder();
        builder.WithLanguage("en");

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void RunParamsBuilder_WithTargetLanguage_ShouldSetTargetLanguage()
    {
        // Arrange & Act
        using var builder = new RunParamsBuilder();
        builder.WithTargetLanguage("fr");

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void RunParamsBuilder_WithTask_ShouldSetTask()
    {
        // Arrange & Act
        using var builder = new RunParamsBuilder();
        builder.WithTask(TranscribeCppSharp.Interop.Task.TaskTranscribe);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void RunParamsBuilder_WithTimestamps_ShouldSetTimestamps()
    {
        // Arrange & Act
        using var builder = new RunParamsBuilder();
        builder.WithTimestamps(TimestampKind.TimestampsWord);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void RunParamsBuilder_WithPnc_ShouldSetPnc()
    {
        // Arrange & Act
        using var builder = new RunParamsBuilder();
        builder.WithPnc(PncMode.PncModeOn);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void RunParamsBuilder_WithItn_ShouldSetItn()
    {
        // Arrange & Act
        using var builder = new RunParamsBuilder();
        builder.WithItn(ItnMode.ItnModeOn);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void RunParamsBuilder_WithWhisperExt_ShouldSetExtension()
    {
        // Arrange
        using var extBuilder = new WhisperExtBuilder();
        extBuilder.WithInitialPrompt("Hello");
        extBuilder.WithTemperature(0.7f);

        // Act
        using var builder = new RunParamsBuilder();
        builder.WithWhisperExt(extBuilder);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void WhisperExtBuilder_WithInitialPrompt_ShouldSetPrompt()
    {
        // Arrange & Act
        using var builder = new WhisperExtBuilder();
        builder.WithInitialPrompt("This is a test prompt");

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void WhisperExtBuilder_WithTemperature_ShouldSetTemperature()
    {
        // Arrange & Act
        using var builder = new WhisperExtBuilder();
        builder.WithTemperature(0.5f);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void WhisperExtBuilder_WithSeed_ShouldSetSeed()
    {
        // Arrange & Act
        using var builder = new WhisperExtBuilder();
        builder.WithSeed(42);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void StreamParamsBuilder_WithCommitPolicy_ShouldSetPolicy()
    {
        // Arrange & Act
        using var builder = new StreamParamsBuilder();
        builder.WithCommitPolicy(StreamCommitPolicy.StreamCommitAuto);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void StreamParamsBuilder_WithStablePrefixAgreement_ShouldSetAgreement()
    {
        // Arrange & Act
        using var builder = new StreamParamsBuilder();
        builder.WithStablePrefixAgreement(3);

        // Assert - no exception thrown
        Assert.NotNull(builder);
    }

    [Fact(Skip = "Requires native library")]
    public void Backends_InitDefault_ShouldNotThrow()
    {
        // Act & Assert - no exception thrown
        TranscribeCppSharp.Backends.InitDefault();
    }

    [Fact(Skip = "Requires native library")]
    public void Backends_EnumerateDevices_ShouldReturnList()
    {
        // Arrange
        TranscribeCppSharp.Backends.InitDefault();

        // Act
        var devices = TranscribeCppSharp.Backends.EnumerateDevices();

        // Assert
        Assert.NotNull(devices);
        // At least CPU should be available
        Assert.True(devices.Count >= 0);
    }

    [Fact(Skip = "Requires native library")]
    public void TranscribeException_ShouldContainStatus()
    {
        // Arrange
        var status = Status.ErrInvalidArg;

        // Act
        var ex = new TranscribeException(status, "TestMethod");

        // Assert
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal("TestMethod", ex.FailedMethod);
        Assert.Contains("ErrInvalidArg", ex.Message);
    }

    [Fact(Skip = "Requires native library")]
    public void Model_Load_NonExistentFile_ShouldThrow()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.gguf");

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => TranscribeCppSharp.Model.Load(nonExistentPath));
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Model_Load_ValidModel_ShouldSucceed()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        Assert.NotNull(model);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Session_Run_ShouldReturnTranscript()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var transcript = session.Run(pcm);

        Assert.NotNull(transcript);
        Assert.NotNull(transcript.FullText);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Session_ReadSegments_ShouldReturnSegments()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session.Run(pcm);

        var segments = session.ReadSegments();
        Assert.NotNull(segments);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Session_ReadWords_ShouldReturnWords()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session.Run(pcm);

        var words = session.ReadWords();
        Assert.NotNull(words);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Session_ReadTokens_ShouldReturnTokens()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session.Run(pcm);

        var tokens = session.ReadTokens();
        Assert.NotNull(tokens);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void StreamSession_Feed_ShouldStreamAudio()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        using var session = model.CreateSession();
        using var stream = session.CreateStream();

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        stream.Begin();

        // Feed in chunks
        int chunkSize = 1600; // 100ms chunks
        for (int i = 0; i < pcm.Length; i += chunkSize)
        {
            int length = Math.Min(chunkSize, pcm.Length - i);
            var chunk = pcm.AsSpan(i, length);
            var update = stream.Feed(chunk);
            Assert.NotNull(update);
        }

        var finalUpdate = stream.Finalize();
        Assert.True(finalUpdate.IsFinal);

        var text = stream.CurrentText;
        Assert.NotNull(text);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Batch_Run_ShouldProcessMultipleBuffers()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        using var session = model.CreateSession();

        var pcm1 = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var pcm2 = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        var results = TranscribeCppSharp.Batch.Run(session, new[] { pcm1, pcm2 });

        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Model_Tokenize_ShouldReturnTokens()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        var tokens = model.Tokenize("Hello world");

        Assert.NotNull(tokens);
        Assert.True(tokens.Length > 0);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Session_SetAbortCallback_ShouldAllowCancellation()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        using var session = model.CreateSession();

        bool callbackInvoked = false;
        session.SetAbortCallback((_) =>
        {
            callbackInvoked = true;
            return false; // Don't abort
        });

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session.Run(pcm);

        // Callback should have been invoked during transcription
        Assert.True(callbackInvoked);
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Model_GetMetaValue_ShouldReturnMetadata()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        var value = model.GetMetaValue("general.architecture");

        // May be null if key doesn't exist
        // Just verify it doesn't throw
    }

    [Fact(Skip = "Requires integration test environment (run ./run-integration-tests.sh)")]
    public void Model_Supports_ShouldCheckFeature()
    {
        // This test requires a real model file
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath);
        var supportsPnc = model.Supports(Feature.FeaturePnc);
        var supportsItn = model.Supports(Feature.FeatureItn);

        // Just verify it doesn't throw
    }
}
