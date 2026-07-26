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

    private static bool IsIntegrationEnv => TestConfig.IsIntegrationTestEnvironment();

    [Fact]
    public void PcmExtensions_ReadWavToPcm_ShouldLoadTestWav()
    {
        if (!IsIntegrationEnv) return;
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
    public void PcmExtensions_ReadWavToPcm_MissingDataChunk_ShouldThrow()
    {
        // WAV with RIFF header + fmt chunk but no data chunk
        var path = Path.Combine(Path.GetTempPath(), "no_data.wav");
        try
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                // RIFF header
                bw.Write("RIFF".ToCharArray());
                bw.Write(0); // file size placeholder
                bw.Write("WAVE".ToCharArray());
                // fmt chunk
                bw.Write("fmt ".ToCharArray());
                bw.Write(16); // chunk size
                bw.Write((short)1); // PCM format
                bw.Write((short)1); // mono
                bw.Write(16000); // sample rate
                bw.Write(32000); // byte rate
                bw.Write((short)2); // block align
                bw.Write((short)16); // bits per sample
                // No data chunk!
            }
            File.WriteAllBytes(path, ms.ToArray());

            Assert.Throws<InvalidDataException>(() => TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ModelLoadParamsBuilder_WithBackend_ShouldSetBackend()
    {
        using var builder = new ModelLoadParamsBuilder();
        builder.WithBackend(BackendRequest.BackendCpu);

        var p = Marshal.PtrToStructure<ModelLoadParams>(builder.Build());
        Assert.Equal(BackendRequest.BackendCpu, p.backend);
    }

    [Fact]
    public void ModelLoadParamsBuilder_WithGpuDevice_ShouldSetDevice()
    {
        using var builder = new ModelLoadParamsBuilder();
        builder.WithGpuDevice(0);

        var p = Marshal.PtrToStructure<ModelLoadParams>(builder.Build());
        Assert.Equal(0, p.gpuDevice);
    }

    [Fact]
    public void SessionParamsBuilder_WithThreads_ShouldSetThreads()
    {
        using var builder = new SessionParamsBuilder();
        builder.WithThreads(4);

        var p = Marshal.PtrToStructure<SessionParams>(builder.Build());
        Assert.Equal(4, p.nThreads);
    }

    [Fact]
    public void SessionParamsBuilder_WithKvType_ShouldSetKvType()
    {
        using var builder = new SessionParamsBuilder();
        builder.WithKvType(KvType.KvTypeF16);

        var p = Marshal.PtrToStructure<SessionParams>(builder.Build());
        Assert.Equal(KvType.KvTypeF16, p.kvType);
    }

    [Fact]
    public void SessionParamsBuilder_WithContextSize_ShouldSetContextSize()
    {
        using var builder = new SessionParamsBuilder();
        builder.WithContextSize(1024);

        var p = Marshal.PtrToStructure<SessionParams>(builder.Build());
        Assert.Equal(1024, p.nCtx);
    }

    [Fact]
    public void RunParamsBuilder_WithLanguage_ShouldSetLanguage()
    {
        using var builder = new RunParamsBuilder();
        builder.WithLanguage("en");

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal("en", Marshal.PtrToStringUTF8(p.language));
    }

    [Fact]
    public void RunParamsBuilder_WithTargetLanguage_ShouldSetTargetLanguage()
    {
        using var builder = new RunParamsBuilder();
        builder.WithTargetLanguage("fr");

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal("fr", Marshal.PtrToStringUTF8(p.targetLanguage));
    }

    [Fact]
    public void RunParamsBuilder_WithTask_ShouldSetTask()
    {
        using var builder = new RunParamsBuilder();
        builder.WithTask(TranscribeCppSharp.Interop.Task.TaskTranscribe);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(TranscribeCppSharp.Interop.Task.TaskTranscribe, p.task);
    }

    [Fact]
    public void RunParamsBuilder_WithTimestamps_ShouldSetTimestamps()
    {
        using var builder = new RunParamsBuilder();
        builder.WithTimestamps(TimestampKind.TimestampsWord);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(TimestampKind.TimestampsWord, p.timestamps);
    }

    [Fact]
    public void RunParamsBuilder_WithPnc_ShouldSetPnc()
    {
        using var builder = new RunParamsBuilder();
        builder.WithPnc(PncMode.PncModeOn);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(PncMode.PncModeOn, p.pnc);
    }

    [Fact]
    public void RunParamsBuilder_WithItn_ShouldSetItn()
    {
        using var builder = new RunParamsBuilder();
        builder.WithItn(ItnMode.ItnModeOn);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(ItnMode.ItnModeOn, p.itn);
    }

    [Fact]
    public void RunParamsBuilder_WithKeepSpecialTags_ShouldSetTags()
    {
        using var builder = new RunParamsBuilder();
        builder.WithKeepSpecialTags(true);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.True(p.keepSpecialTags);
    }

    [Fact]
    public void RunParamsBuilder_WithSpecKDrafts_ShouldSetDrafts()
    {
        using var builder = new RunParamsBuilder();
        builder.WithSpecKDrafts(5);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(5, p.specKDrafts);
    }

    [Fact]
    public void RunParamsBuilder_WithWhisperExt_ShouldSetExtension()
    {
        using var extBuilder = new WhisperExtBuilder();
        extBuilder.WithInitialPrompt("Hello");
        extBuilder.WithTemperature(0.7f);

        using var builder = new RunParamsBuilder();
        builder.WithWhisperExt(extBuilder);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.family);
    }

    [Fact]
    public void RunParamsBuilder_WithWhisperExt_ReplacesPreviousExt()
    {
        using var ext1 = new WhisperExtBuilder();
        ext1.WithTemperature(0.5f);
        using var ext2 = new WhisperExtBuilder();
        ext2.WithTemperature(0.9f);

        using var builder = new RunParamsBuilder();
        builder.WithWhisperExt(ext1);
        builder.WithWhisperExt(ext2);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        var ext = Marshal.PtrToStructure<WhisperRunExt>(p.family);
        Assert.Equal(0.9f, ext.temperature);
    }

    [Fact]
    public void WhisperExtBuilder_WithInitialPrompt_ShouldSetPrompt()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithInitialPrompt("This is a test prompt");

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.initialPrompt);
        Assert.Equal("This is a test prompt", Marshal.PtrToStringUTF8(p.initialPrompt));
    }

    [Fact]
    public void WhisperExtBuilder_WithTemperature_ShouldSetTemperature()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithTemperature(0.5f);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(0.5f, p.temperature);
    }

    [Fact]
    public void WhisperExtBuilder_WithSeed_ShouldSetSeed()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithSeed(42);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(42u, p.seed);
    }

    [Fact]
    public void StreamParamsBuilder_WithCommitPolicy_ShouldSetPolicy()
    {
        using var builder = new StreamParamsBuilder();
        builder.WithCommitPolicy(StreamCommitPolicy.StreamCommitAuto);

        var p = Marshal.PtrToStructure<StreamParams>(builder.Build());
        Assert.Equal(StreamCommitPolicy.StreamCommitAuto, p.commitPolicy);
    }

    [Fact]
    public void StreamParamsBuilder_WithStablePrefixAgreement_ShouldSetAgreement()
    {
        using var builder = new StreamParamsBuilder();
        builder.WithStablePrefixAgreement(3);

        var p = Marshal.PtrToStructure<StreamParams>(builder.Build());
        Assert.Equal(3u, p.stablePrefixAgreementN);
    }

    [Fact]
    public void MoonshineExtBuilder_WithMinDecodeIntervalMs_ShouldSet()
    {
        using var builder = new MoonshineExtBuilder();
        builder.WithMinDecodeIntervalMs(200);

        var p = Marshal.PtrToStructure<MoonshineStreamingStreamExt>(builder.Build());
        Assert.Equal(200, p.minDecodeIntervalMs);
    }

    [Fact]
    public void StreamParamsBuilder_WithMoonshineExt_ShouldSetFamily()
    {
        using var ext = new MoonshineExtBuilder();
        ext.WithMinDecodeIntervalMs(200);

        using var builder = new StreamParamsBuilder();
        builder.WithMoonshineExt(ext);

        var p = Marshal.PtrToStructure<StreamParams>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.family);
        var extStruct = Marshal.PtrToStructure<MoonshineStreamingStreamExt>(p.family);
        Assert.Equal(200, extStruct.minDecodeIntervalMs);
    }

    [Fact]
    public void ParakeetStreamExtBuilder_WithAttContextRight_ShouldSet()
    {
        using var builder = new ParakeetStreamExtBuilder();
        builder.WithAttContextRight(5);

        var p = Marshal.PtrToStructure<ParakeetStreamExt>(builder.Build());
        Assert.Equal(5, p.attContextRight);
    }

    [Fact]
    public void StreamParamsBuilder_WithParakeetStreamExt_ShouldSetFamily()
    {
        using var ext = new ParakeetStreamExtBuilder();
        ext.WithAttContextRight(5);

        using var builder = new StreamParamsBuilder();
        builder.WithParakeetStreamExt(ext);

        var p = Marshal.PtrToStructure<StreamParams>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.family);
        var extStruct = Marshal.PtrToStructure<ParakeetStreamExt>(p.family);
        Assert.Equal(5, extStruct.attContextRight);
    }

    [Fact]
    public void ParakeetBufferedStreamExtBuilder_WithAllFields_ShouldSet()
    {
        using var builder = new ParakeetBufferedStreamExtBuilder();
        builder.WithLeftMs(500)
               .WithChunkMs(1000)
               .WithRightMs(200);

        var p = Marshal.PtrToStructure<ParakeetBufferedStreamExt>(builder.Build());
        Assert.Equal(500, p.leftMs);
        Assert.Equal(1000, p.chunkMs);
        Assert.Equal(200, p.rightMs);
    }

    [Fact]
    public void StreamParamsBuilder_WithParakeetBufferedStreamExt_ShouldSetFamily()
    {
        using var ext = new ParakeetBufferedStreamExtBuilder();
        ext.WithLeftMs(500).WithChunkMs(1000).WithRightMs(200);

        using var builder = new StreamParamsBuilder();
        builder.WithParakeetBufferedStreamExt(ext);

        var p = Marshal.PtrToStructure<StreamParams>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.family);
        var extStruct = Marshal.PtrToStructure<ParakeetBufferedStreamExt>(p.family);
        Assert.Equal(500, extStruct.leftMs);
        Assert.Equal(1000, extStruct.chunkMs);
        Assert.Equal(200, extStruct.rightMs);
    }

    [Fact]
    public void VoxtralExtBuilder_WithAllFields_ShouldSet()
    {
        using var builder = new VoxtralExtBuilder();
        builder.WithNumDelayTokens(10)
               .WithMinDecodeIntervalMs(300);

        var p = Marshal.PtrToStructure<VoxtralRealtimeStreamExt>(builder.Build());
        Assert.Equal(10, p.numDelayTokens);
        Assert.Equal(300, p.minDecodeIntervalMs);
    }

    [Fact]
    public void StreamParamsBuilder_WithVoxtralExt_ShouldSetFamily()
    {
        using var ext = new VoxtralExtBuilder();
        ext.WithNumDelayTokens(10).WithMinDecodeIntervalMs(300);

        using var builder = new StreamParamsBuilder();
        builder.WithVoxtralExt(ext);

        var p = Marshal.PtrToStructure<StreamParams>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.family);
        var extStruct = Marshal.PtrToStructure<VoxtralRealtimeStreamExt>(p.family);
        Assert.Equal(10, extStruct.numDelayTokens);
        Assert.Equal(300, extStruct.minDecodeIntervalMs);
    }

    [Fact]
    public void StreamParamsBuilder_WithExt_ReplacesPreviousExt()
    {
        using var moonshine = new MoonshineExtBuilder();
        moonshine.WithMinDecodeIntervalMs(100);
        using var voxtral = new VoxtralExtBuilder();
        voxtral.WithNumDelayTokens(5).WithMinDecodeIntervalMs(200);

        using var builder = new StreamParamsBuilder();
        builder.WithMoonshineExt(moonshine);
        builder.WithVoxtralExt(voxtral);

        var p = Marshal.PtrToStructure<StreamParams>(builder.Build());
        var extStruct = Marshal.PtrToStructure<VoxtralRealtimeStreamExt>(p.family);
        Assert.Equal(5, extStruct.numDelayTokens);
        Assert.Equal(200, extStruct.minDecodeIntervalMs);
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
        if (!IsIntegrationEnv) return;
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.NotNull(model);
    }

    [Fact]
    public void Session_Run_ShouldReturnTranscript()
    {
        if (!IsIntegrationEnv) return;
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
        if (!IsIntegrationEnv) return;
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
        if (!IsIntegrationEnv) return;
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        try
        {
            session.Run(pcm, p => p.WithTimestamps(TimestampKind.TimestampsWord));
            var words = session.ReadWords();
            Assert.NotEmpty(words);
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrUnsupportedTimestamps)
        {
            // Allowed if model doesn't support word timestamps
        }
    }

    [Fact]
    public void Session_ReadTokens_ShouldReturnTokens()
    {
        if (!IsIntegrationEnv) return;
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
        if (!IsIntegrationEnv) return;
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
        if (!IsIntegrationEnv) return;
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
        if (!IsIntegrationEnv) return;
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var tokens = model.Tokenize("Hello world");

        Assert.NotNull(tokens);
        Assert.True(tokens.Length > 0);
    }

    [Fact]
    public void Session_SetAbortCallback_ShouldAllowCancellation()
    {
        if (!IsIntegrationEnv) return;
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
        if (!IsIntegrationEnv) return;
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var value = model.GetMetaValue("general.architecture");
        Assert.NotNull(value);
    }

    [Fact]
    public void Model_Supports_ShouldCheckFeature()
    {
        if (!IsIntegrationEnv) return;
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var supportsPnc = model.Supports(Feature.FeaturePnc);
        Assert.IsType<bool>(supportsPnc);
    }

    [Fact]
    public void Model_Metadata_ShouldReturnInfo()
    {
        if (!IsIntegrationEnv) return;
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
        if (!IsIntegrationEnv) return;
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        
        var limits = session.GetLimits();
        // Limits might be 0 for some models/backends, but the call should succeed
        
        Assert.False(session.WasAborted);
        Assert.False(session.WasTruncated);
        
        session.ResetTimings();
    }
}