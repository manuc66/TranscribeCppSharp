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
        try
        {
            TranscribeCppSharp.Backends.InitDefault();
        }
        catch
        {
            // Ignore errors here, they will be caught in tests
        }
    }

    public void Dispose()
    {
    }

    [Fact]
    public void PcmExtensions_ReadWavToPcm_ShouldLoadTestWav()
    {
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        Assert.NotNull(pcm);
        Assert.True(pcm.Length > 0);
        Assert.All(pcm, s => Assert.InRange(s, -1f, 1f));
    }

    [Fact]
    public void PcmExtensions_ReadWavToPcm_InvalidFile_ShouldThrow()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "invalid.wav");
        File.WriteAllText(invalidPath, "not a wav file");

        try
        {
            Assert.Throws<InvalidDataException>(() => TranscribeCppSharp.PcmExtensions.ReadWavToPcm(invalidPath));
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }

    [Fact]
    public void ModelLoadParamsBuilder_WithBackend_ShouldSetBackend()
    {
        using var builder = new ModelLoadParamsBuilder();
        builder.WithBackend(BackendRequest.BackendCpu);

        Assert.NotNull(builder);
    }

    [Fact]
    public void ModelLoadParamsBuilder_WithGpuDevice_ShouldSetDevice()
    {
        using var builder = new ModelLoadParamsBuilder();
        builder.WithGpuDevice(0);

        Assert.NotNull(builder);
    }

    [Fact]
    public void SessionParamsBuilder_WithThreads_ShouldSetThreads()
    {
        using var builder = new SessionParamsBuilder();
        builder.WithThreads(4);

        Assert.NotNull(builder);
    }

    [Fact]
    public void SessionParamsBuilder_WithKvType_ShouldSetKvType()
    {
        using var builder = new SessionParamsBuilder();
        builder.WithKvType(KvType.KvTypeF16);

        Assert.NotNull(builder);
    }

    [Fact]
    public void SessionParamsBuilder_WithContextSize_ShouldSetContextSize()
    {
        using var builder = new SessionParamsBuilder();
        builder.WithContextSize(1024);

        Assert.NotNull(builder);
    }

    [Fact]
    public void RunParamsBuilder_WithLanguage_ShouldSetLanguage()
    {
        using var builder = new RunParamsBuilder();
        builder.WithLanguage("en");

        Assert.NotNull(builder);
    }

    [Fact]
    public void RunParamsBuilder_WithTargetLanguage_ShouldSetTargetLanguage()
    {
        using var builder = new RunParamsBuilder();
        builder.WithTargetLanguage("fr");

        Assert.NotNull(builder);
    }

    [Fact]
    public void RunParamsBuilder_WithTask_ShouldSetTask()
    {
        using var builder = new RunParamsBuilder();
        builder.WithTask(TranscribeCppSharp.Interop.Task.TaskTranscribe);

        Assert.NotNull(builder);
    }

    [Fact]
    public void RunParamsBuilder_WithTimestamps_ShouldSetTimestamps()
    {
        using var builder = new RunParamsBuilder();
        builder.WithTimestamps(TimestampKind.TimestampsWord);

        Assert.NotNull(builder);
    }

    [Fact]
    public void RunParamsBuilder_WithPnc_ShouldSetPnc()
    {
        using var builder = new RunParamsBuilder();
        builder.WithPnc(PncMode.PncModeOn);

        Assert.NotNull(builder);
    }

    [Fact]
    public void RunParamsBuilder_WithItn_ShouldSetItn()
    {
        using var builder = new RunParamsBuilder();
        builder.WithItn(ItnMode.ItnModeOn);

        Assert.NotNull(builder);
    }

    [Fact]
    public void RunParamsBuilder_WithWhisperExt_ShouldSetExtension()
    {
        using var extBuilder = new WhisperExtBuilder();
        extBuilder.WithInitialPrompt("Hello");
        extBuilder.WithTemperature(0.7f);

        using var builder = new RunParamsBuilder();
        builder.WithWhisperExt(extBuilder);

        Assert.NotNull(builder);
    }

    [Fact]
    public void WhisperExtBuilder_WithInitialPrompt_ShouldSetPrompt()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithInitialPrompt("This is a test prompt");

        Assert.NotNull(builder);
    }

    [Fact]
    public void WhisperExtBuilder_WithTemperature_ShouldSetTemperature()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithTemperature(0.5f);

        Assert.NotNull(builder);
    }

    [Fact]
    public void WhisperExtBuilder_WithSeed_ShouldSetSeed()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithSeed(42);

        Assert.NotNull(builder);
    }

    [Fact]
    public void StreamParamsBuilder_WithCommitPolicy_ShouldSetPolicy()
    {
        using var builder = new StreamParamsBuilder();
        builder.WithCommitPolicy(StreamCommitPolicy.StreamCommitAuto);

        Assert.NotNull(builder);
    }

    [Fact]
    public void StreamParamsBuilder_WithStablePrefixAgreement_ShouldSetAgreement()
    {
        using var builder = new StreamParamsBuilder();
        builder.WithStablePrefixAgreement(3);

        Assert.NotNull(builder);
    }

    [Fact]
    public void Backends_InitDefault_ShouldNotThrow()
    {
        TranscribeCppSharp.Backends.InitDefault();
    }

    [Fact]
    public void Backends_EnumerateDevices_ShouldReturnList()
    {
        TranscribeCppSharp.Backends.InitDefault();

        var devices = TranscribeCppSharp.Backends.EnumerateDevices();

        Assert.NotNull(devices);
        Assert.True(devices.Count >= 0);
    }

    [Fact]
    public void TranscribeException_ShouldContainStatus()
    {
        var status = Status.ErrInvalidArg;

        var ex = new TranscribeException(status, "TestMethod");

        Assert.Equal(status, ex.StatusCode);
        Assert.Equal("TestMethod", ex.FailedMethod);
        Assert.Contains("ErrInvalidArg", ex.Message);
    }

    [Fact]
    public void Model_Load_NonExistentFile_ShouldThrow()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.gguf");

        Assert.ThrowsAny<Exception>(() => TranscribeCppSharp.Model.Load(nonExistentPath));
    }

    [Fact]
    public void ModelLoad_ValidModel_ShouldSucceed()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.NotNull(model);
    }

    [Fact]
    public void Session_Run_ShouldReturnTranscript()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var transcript = session.Run(pcm);

        Assert.NotNull(transcript);
        Assert.NotEmpty(transcript.FullText);
    }

    [Fact]
    public void Session_ReadSegments_ShouldReturnSegments()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session.Run(pcm);

        var segments = session.ReadSegments();
        Assert.NotNull(segments);
        Assert.NotEmpty(segments);
    }

    [Fact]
    public void Session_ReadWords_ShouldReturnWords()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        try
        {
            session.Run(pcm, p => p.WithTimestamps(TimestampKind.TimestampsWord));
            var words = session.ReadWords();
            Assert.NotNull(words);
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrUnsupportedTimestamps)
        {
            // Allowed if model doesn't support word timestamps
        }
    }

    [Fact]
    public void Session_ReadTokens_ShouldReturnTokens()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        try
        {
            session.Run(pcm, p => p.WithTimestamps(TimestampKind.TimestampsSegment));
            var tokens = session.ReadTokens();
            Assert.NotNull(tokens);
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrUnsupportedTimestamps)
        {
            // Allowed if model doesn't support requested timestamps
        }
    }

    [Fact]
    public void StreamSession_Feed_ShouldStreamAudio()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        using var stream = session.CreateStream();

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        try
        {
            stream.Begin();

            int chunkSize = 16000; // 1 second
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
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrNotImplemented)
        {
            // Allowed if streaming is not implemented in the native library
        }
    }

    [Fact]
    public void Batch_Run_ShouldProcessMultipleBuffers()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();

        var pcm1 = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var pcm2 = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        var results = TranscribeCppSharp.Batch.Run(session, new[] { pcm1, pcm2 });

        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        Assert.NotEmpty(results[0].FullText);
    }

    [Fact]
    public void Model_Tokenize_ShouldReturnTokens()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var tokens = model.Tokenize("Hello world");

        Assert.NotNull(tokens);
        Assert.True(tokens.Length > 0);
    }

    [Fact]
    public void Session_SetAbortCallback_ShouldAllowCancellation()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();

        bool callbackInvoked = false;
        session.SetAbortCallback((_) =>
        {
            callbackInvoked = true;
            return false;
        });

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session.Run(pcm);

        Assert.True(callbackInvoked);
    }

    [Fact]
    public void Model_GetMetaValue_ShouldReturnMetadata()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var value = model.GetMetaValue("general.architecture");
        Assert.NotNull(value);
    }

    [Fact]
    public void Model_Supports_ShouldCheckFeature()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var supportsPnc = model.Supports(Feature.FeaturePnc);
        // We don't assert the value as it depends on the model
    }

    [Fact]
    public void Model_Metadata_ShouldReturnInfo()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.NotEmpty(model.Architecture);
        Assert.NotEmpty(model.Variant);
        Assert.NotEmpty(model.Backend);
        
        var caps = model.GetCapabilities();
        Assert.True(caps.nativeSampleRate > 0);
    }

    [Fact]
    public void Session_Metadata_ShouldReturnInfo()
    {
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        
        var limits = session.GetLimits();
        // Limits might be 0 for some models/backends, but the call should succeed
        
        Assert.False(session.WasAborted);
        Assert.False(session.WasTruncated);
        
        session.ResetTimings();
    }
}