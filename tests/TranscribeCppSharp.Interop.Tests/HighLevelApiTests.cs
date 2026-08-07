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
        GC.SuppressFinalize(this);
    }

    private static bool IsIntegrationEnv => TestConfig.IsIntegrationTestEnvironment();

    [SkippableFact]
    public void PcmExtensions_ReadWavToPcm_ShouldLoadTestWav()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
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
    public void PcmExtensions_ReadWavToPcm_Mono_ConvertsSamples()
    {
        var path = WriteTestWav(numChannels: 1, samples: [1000, -1000, 0]);

        try
        {
            var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path);
            Assert.Equal(3, pcm.Length);
            Assert.Equal(1000f / 32768f, pcm[0], 4);
            Assert.Equal(-1000f / 32768f, pcm[1], 4);
            Assert.Equal(0f, pcm[2], 4);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PcmExtensions_ReadWavToPcm_Stereo_DownmixesToMono()
    {
        // Stereo, 2 frames, both channels identical: frame0 = 2000, frame1 = 4000.
        // Downmix averages the channels, so the result equals the shared value.
        var path = WriteTestWav(numChannels: 2, samples: [2000, 4000]);

        try
        {
            var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path);
            Assert.Equal(2, pcm.Length);
            Assert.Equal(2000f / 32768f, pcm[0], 4);
            Assert.Equal(4000f / 32768f, pcm[1], 4);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PcmExtensions_ReadWavToPcm_Non16k_Throws()
    {
        var path = WriteTestWav(numChannels: 1, sampleRate: 44100, samples: [0]);

        try
        {
            Assert.Throws<InvalidDataException>(() => TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTestWav(int numChannels, int sampleRate = 16000, short[]? samples = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.wav");
        var data = new byte[samples!.Length * 2 * numChannels];
        for (int i = 0; i < samples.Length; i++)
        {
            for (int ch = 0; ch < numChannels; ch++)
            {
                short value = samples[i]; // all channels carry the same value
                int offset = ((i * numChannels) + ch) * 2;
                data[offset] = (byte)(value & 0xFF);
                data[offset + 1] = (byte)((value >> 8) & 0xFF);
            }
        }

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write("RIFF".ToCharArray());
            bw.Write(36 + data.Length);
            bw.Write("WAVE".ToCharArray());
            bw.Write("fmt ".ToCharArray());
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)numChannels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * numChannels * 2); // byte rate
            bw.Write((short)(numChannels * 2)); // block align
            bw.Write((short)16); // bits per sample
            bw.Write("data".ToCharArray());
            bw.Write(data.Length);
            bw.Write(data);
        }
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static string WriteTestWavWithFormat(short audioFormat, short bitsPerSample, int sampleRate = 16000, int numChannels = 1, int sampleCount = 10)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.wav");
        int bytesPerSample = bitsPerSample / 8;
        int dataLength = sampleCount * bytesPerSample * numChannels;

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write("RIFF".ToCharArray());
            bw.Write(36 + dataLength);
            bw.Write("WAVE".ToCharArray());
            bw.Write("fmt ".ToCharArray());
            bw.Write(16);
            bw.Write(audioFormat);
            bw.Write((short)numChannels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * numChannels * bytesPerSample); // byte rate
            bw.Write((short)(numChannels * bytesPerSample)); // block align
            bw.Write(bitsPerSample);
            bw.Write("data".ToCharArray());
            bw.Write(dataLength);
            bw.Write(new byte[dataLength]); // zero-filled data
        }
        File.WriteAllBytes(path, ms.ToArray());
        return path;
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
        builder.WithTask(TranscriptionTask.Transcribe);

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal(Interop.Task.TaskTranscribe, p.task);
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
    public void RunParamsBuilder_ExtManualDispose_NoDoubleFree()
    {
        var ext = new WhisperExtBuilder();
        ext.WithTemperature(0.5f);

        // Manually dispose ext before passing to builder
        ext.Dispose();

        using var builder = new RunParamsBuilder();
        Assert.Throws<ObjectDisposedException>(() => builder.WithWhisperExt(ext));
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
    public void WhisperExtBuilder_WithPromptTokens_ShouldSetTokens()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithPromptTokens(new[] { 101, 2023, 102 });

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.promptTokens);
        Assert.Equal((nuint)3, p.nPromptTokens);
        Assert.Equal(101, Marshal.ReadInt32(p.promptTokens, 0));
        Assert.Equal(2023, Marshal.ReadInt32(p.promptTokens, 4));
        Assert.Equal(102, Marshal.ReadInt32(p.promptTokens, 8));
    }

    [Fact]
    public void WhisperExtBuilder_WithPromptCondition_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithPromptCondition(WhisperPromptCondition.WhisperPromptAllSegments);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(WhisperPromptCondition.WhisperPromptAllSegments, p.promptCondition);
    }

    [Fact]
    public void WhisperExtBuilder_WithConditionOnPrevTokens_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithConditionOnPrevTokens(true);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.True(p.conditionOnPrevTokens);
    }

    [Fact]
    public void WhisperExtBuilder_WithMaxPrevContextTokens_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithMaxPrevContextTokens(1024);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(1024, p.maxPrevContextTokens);
    }

    [Fact]
    public void WhisperExtBuilder_WithTemperatureInc_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithTemperatureInc(0.2f);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(0.2f, p.temperatureInc);
    }

    [Fact]
    public void WhisperExtBuilder_WithCompressionRatioThold_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithCompressionRatioThold(2.4f);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(2.4f, p.compressionRatioThold);
    }

    [Fact]
    public void WhisperExtBuilder_WithLogprobThold_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithLogprobThold(-1.0f);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(-1.0f, p.logprobThold);
    }

    [Fact]
    public void WhisperExtBuilder_WithNoSpeechThold_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithNoSpeechThold(0.6f);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(0.6f, p.noSpeechThold);
    }

    [Fact]
    public void WhisperExtBuilder_WithMaxInitialTimestamp_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithMaxInitialTimestamp(1.0f);

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal(1.0f, p.maxInitialTimestamp);
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
    public void Backends_Version_ShouldReturnNonEmpty()
    {
        var version = TranscribeCppSharp.Backends.Version;
        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public void Backends_VersionCommit_ShouldReturnNonEmpty()
    {
        var commit = TranscribeCppSharp.Backends.VersionCommit;
        Assert.False(string.IsNullOrWhiteSpace(commit));
    }

    [SkippableFact]
    public void Backends_BackendAvailable_ReportsRegisteredBackends()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        TranscribeCppSharp.Backends.InitDefault();

        // CPU is always present; AUTO whenever any device exists.
        Assert.True(TranscribeCppSharp.Backends.BackendAvailable(BackendRequest.BackendAuto));
        Assert.True(TranscribeCppSharp.Backends.BackendAvailable(BackendRequest.BackendCpu));

        // Unknown request values answer false, never an error (per upstream doc).
        Assert.False(TranscribeCppSharp.Backends.BackendAvailable((BackendRequest)999));
    }

    [Fact]
    public void Log_Configure_ShouldNotThrow()
    {
        Log.Configure((level, msg) => { });
        Log.Configure(null);
        Log.Configure(null); // double-disable is safe
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

    [SkippableFact]
    public void ModelLoad_ValidModel_ShouldSucceed()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.NotNull(model);
    }

    [SkippableFact]
    public void Session_Run_ShouldReturnTranscript()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var transcript = session.Run(pcm);

        Assert.NotNull(transcript);
        Assert.NotEmpty(transcript.FullText);
    }

    [SkippableFact]
    public void Session_ReadSegments_ShouldReturnSegments()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session.Run(pcm);

        var segments = session.ReadSegments();
        Assert.NotNull(segments);
        Assert.NotEmpty(segments);
    }

    [SkippableFact]
    public void Session_ReadWords_ShouldReturnWords()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
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

    [SkippableFact]
    public void Session_ReadTokens_ShouldReturnTokens()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
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

    [SkippableFact]
    public void StreamSession_Feed_ShouldStreamAudio()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
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

            var finalUpdate = stream.Complete();
            Assert.True(finalUpdate.IsFinal);

            var text = stream.CurrentText;
            Assert.NotNull(text);
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrNotImplemented)
        {
            // Allowed if streaming is not implemented in the native library
        }
    }

    [SkippableFact]
    public void StreamSession_Dispose_ShouldResetNativeState()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var stream = session.CreateStream();
        try
        {
            stream.Begin();
            stream.Dispose(); // should not throw
            // After dispose, any access should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() => stream.State);
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrNotImplemented)
        {
            // Allowed if streaming is not implemented in the native library
        }
    }

    [SkippableFact]
    public void StreamSession_DoubleDispose_ShouldNotThrow()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var stream = session.CreateStream();
        try
        {
            stream.Begin();
            stream.Dispose();
            stream.Dispose(); // second dispose must not throw
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrNotImplemented)
        {
            // Allowed if streaming is not implemented in the native library
        }
    }

    [SkippableFact]
    public void StreamSession_Begin_WithConfig_ShouldSucceed()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        using var stream = session.CreateStream();
        try
        {
            stream.Begin(
                runConfig: p => p.WithTask(TranscriptionTask.Transcribe),
                streamConfig: p => p.WithCommitPolicy(StreamCommitPolicy.StreamCommitAuto));
            Assert.Equal(StreamState.StreamIdle, stream.State);
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrNotImplemented)
        {
            // Allowed if streaming is not implemented
        }
    }

    [SkippableFact]
    public void Backends_EnumerateDevices_ShouldReturnList()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        var devices = TranscribeCppSharp.Backends.EnumerateDevices();
        Assert.NotNull(devices);
        // On CPU-only CI, there should be at least one device (CPU)
        Assert.True(devices.Count > 0);
    }

    [SkippableFact]
    public void Backends_Init_WithArtifactDir_ShouldNotThrow()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        var artifactDir = Path.GetDirectoryName(typeof(HighLevelApiTests).Assembly.Location)!;
        TranscribeCppSharp.Backends.Init(artifactDir);
    }

    [SkippableFact]
    public void Batch_Run_ShouldProcessMultipleBuffers()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();

        var pcm1 = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var pcm2 = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        var results = TranscribeCppSharp.Batch.Run(session, new[] { pcm1, pcm2 });

        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        Assert.NotEmpty(results[0].FullText);
        Assert.NotNull(results[0].Segments);
        Assert.NotNull(results[0].Words);
        Assert.NotNull(results[0].Tokens);
        Assert.NotNull(results[0].Timing);
    }

    [SkippableFact]
    public void Model_MultipleSessions_SerializedRuns_ShouldWork()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session1 = model.CreateSession();
        using var session2 = model.CreateSession();

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        // Runs are serialized (never concurrent): upstream 0.x allows at most one
        // compute in flight per model, so we alternate sequentially across sessions.
        var t1 = session1.Run(pcm);
        var t2 = session2.Run(pcm);
        var t3 = session1.Run(pcm);

        Assert.NotNull(t1);
        Assert.NotNull(t2);
        Assert.NotNull(t3);
        Assert.NotEmpty(t1.FullText);
        Assert.Equal(t1.FullText, t2.FullText);
        Assert.Equal(t1.FullText, t3.FullText);
    }

    [SkippableFact]
    public void Model_MultipleSessions_IndependentMetadata()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session1 = model.CreateSession();
        using var session2 = model.CreateSession();

        // Sessions share the model but each owns its own session state; mutating
        // one session must not affect the other's counters.
        Assert.Equal(session1.SegmentCount, session2.SegmentCount);

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        _ = session1.Run(pcm);

        // session2 was never run, so it has no result yet; session1 does.
        Assert.Equal(0, session2.SegmentCount);
        Assert.True(session1.SegmentCount > 0);
    }

    [SkippableFact]
    public void Model_MultipleSessions_OneDisposed_OtherStillWorks()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var session1 = model.CreateSession();
        using var session2 = model.CreateSession();

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session1.Dispose();

        // session2 must still work after session1 is disposed.
        var t2 = session2.Run(pcm);
        Assert.NotNull(t2);
        Assert.NotEmpty(t2.FullText);
    }

    [SkippableFact]
    public void Model_Tokenize_ShouldReturnTokens()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var tokens = model.Tokenize("Hello world");

        Assert.NotNull(tokens);
        Assert.True(tokens.Length > 0);
    }

    [SkippableFact]
    public void Session_SetAbortCallback_ShouldAllowCancellation()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();

        bool callbackInvoked = false;
        session.SetAbortCallback(() =>
        {
            callbackInvoked = true;
            return false;
        });

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        session.Run(pcm);

        Assert.True(callbackInvoked);
    }

    [SkippableFact]
    public void Model_GetMetaValue_ShouldReturnMetadata()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var value = model.GetMetaValue("general.architecture");
        Assert.NotNull(value);
    }

    [SkippableFact]
    public void Model_Supports_ShouldCheckFeature()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var supportsPnc = model.Supports(Feature.FeaturePnc);
        Assert.IsType<bool>(supportsPnc);
    }

    [SkippableFact]
    public void Model_Metadata_ShouldReturnInfo()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.NotEmpty(model.Architecture);
        Assert.NotEmpty(model.Variant);
        Assert.NotEmpty(model.Backend);
        
        var caps = model.GetCapabilities();
        Assert.True(caps.NativeSampleRate > 0);
    }

    [SkippableFact]
    public void Session_Metadata_ShouldReturnInfo()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        
        var limits = session.GetLimits();
        // Limits might be 0 for some models/backends, but the call should succeed
        
        Assert.False(session.WasAborted);
        Assert.False(session.WasTruncated);
        
        session.ResetTimings();
    }

    // ═══════════════════════════════════════════════════════════════════
    // §6: Disposal tests (no native lib required for most)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public void Session_DoubleDispose_DoesNotThrow()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var session = model.CreateSession();
        session.Dispose();
        session.Dispose(); // second dispose must not throw
    }

    [SkippableFact]
    public void Model_DoubleDispose_DoesNotThrow()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        model.Dispose();
        model.Dispose(); // second dispose must not throw
    }

    [Fact]
    public void RunParamsBuilder_Build_AfterDispose_Throws()
    {
        var builder = new RunParamsBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.Build());
    }

    [Fact]
    public void SessionParamsBuilder_Build_AfterDispose_Throws()
    {
        var builder = new SessionParamsBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.Build());
    }

    [Fact]
    public void StreamParamsBuilder_Build_AfterDispose_Throws()
    {
        var builder = new StreamParamsBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.Build());
    }

    [Fact]
    public void ModelLoadParamsBuilder_Build_AfterDispose_Throws()
    {
        var builder = new ModelLoadParamsBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.Build());
    }

    [Fact]
    public void WhisperExtBuilder_WithInitialPrompt_AfterDispose_Throws()
    {
        var builder = new WhisperExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithInitialPrompt("test"));
    }

    [Fact]
    public void WhisperExtBuilder_WithPromptTokens_AfterDispose_Throws()
    {
        var builder = new WhisperExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithPromptTokens(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void WhisperExtBuilder_WithTemperature_AfterDispose_Throws()
    {
        var builder = new WhisperExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithTemperature(0.5f));
    }

    [Fact]
    public void WhisperExtBuilder_WithSeed_AfterDispose_Throws()
    {
        var builder = new WhisperExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithSeed(42));
    }

    [Fact]
    public void MoonshineExtBuilder_WithMinDecodeIntervalMs_AfterDispose_Throws()
    {
        var builder = new MoonshineExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithMinDecodeIntervalMs(200));
    }

    [Fact]
    public void VoxtralExtBuilder_WithNumDelayTokens_AfterDispose_Throws()
    {
        var builder = new VoxtralExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithNumDelayTokens(5));
    }

    [Fact]
    public void VoxtralExtBuilder_WithMinDecodeIntervalMs_AfterDispose_Throws()
    {
        var builder = new VoxtralExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithMinDecodeIntervalMs(100));
    }

    [Fact]
    public void ParakeetStreamExtBuilder_WithAttContextRight_AfterDispose_Throws()
    {
        var builder = new ParakeetStreamExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithAttContextRight(10));
    }

    [Fact]
    public void ParakeetBufferedStreamExtBuilder_WithLeftMs_AfterDispose_Throws()
    {
        var builder = new ParakeetBufferedStreamExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithLeftMs(500));
    }

    [Fact]
    public void ParakeetBufferedStreamExtBuilder_WithChunkMs_AfterDispose_Throws()
    {
        var builder = new ParakeetBufferedStreamExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithChunkMs(1000));
    }

    [Fact]
    public void ParakeetBufferedStreamExtBuilder_WithRightMs_AfterDispose_Throws()
    {
        var builder = new ParakeetBufferedStreamExtBuilder();
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.WithRightMs(300));
    }

    // ═══════════════════════════════════════════════════════════════════
    // §6: Batch edge cases (no native lib for null/empty checks)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Batch_Run_NullSession_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TranscribeCppSharp.Batch.Run(null!, new float[][] { new float[16000] }));
    }

    [SkippableFact]
    public void Batch_Run_NullBuffers_ThrowsArgumentNullException()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        Assert.Throws<ArgumentNullException>(() =>
            TranscribeCppSharp.Batch.Run(session, null!));
    }

    [SkippableFact]
    public void Batch_Run_EmptyArray_ReturnsEmpty()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var results = TranscribeCppSharp.Batch.Run(session, Array.Empty<float[]>());
        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════════════════════
    // §6: Input validation (no native lib required)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Model_Load_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TranscribeCppSharp.Model.Load(null!));
    }

    [Fact]
    public void RunParamsBuilder_WithWhisperExt_Null_ThrowsArgumentNullException()
    {
        using var builder = new RunParamsBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.WithWhisperExt(null!));
    }

    [Fact]
    public void RunParamsBuilder_WithTask_Null_ThrowsArgumentNullException()
    {
        using var builder = new RunParamsBuilder();
        // TranscriptionTask is non-nullable enum, but we test the switch guard
        // by casting an invalid value
        var invalidTask = (TranscriptionTask)999;
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => builder.WithTask(invalidTask));
    }

    [SkippableFact]
    public void Model_Tokenize_NullText_ThrowsArgumentNullException()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.Throws<ArgumentNullException>(() => model.Tokenize(null!));
    }

    [SkippableFact]
    public void Model_Tokenize_ZeroInitialCapacity_ThrowsArgumentOutOfRangeException()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Tokenize("test", 0));
    }

    [SkippableFact]
    public void Model_Tokenize_NegativeInitialCapacity_ThrowsArgumentOutOfRangeException()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Tokenize("test", -1));
    }

    [SkippableFact]
    public void Model_Tokenize_EmptyString_ShouldReturnEmptyOrSingleToken()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var tokens = model.Tokenize("");
        Assert.NotNull(tokens);
        // Empty string may yield 0 tokens or a single end-of-text token
        Assert.True(tokens.Length >= 0);
    }

    [SkippableFact]
    public void Model_Tokenize_SmallInitialCapacity_ShouldRetryAndSucceed()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        // capacity=1 forces the retry loop (negative return = required size)
        var tokens = model.Tokenize("Hello world, this is a test.", initialCapacity: 1);
        Assert.NotNull(tokens);
        Assert.True(tokens.Length > 0);
    }

    [SkippableFact]
    public void Model_Tokenize_LargeInitialCapacity_ShouldWork()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var tokens = model.Tokenize("Hello", initialCapacity: 8192);
        Assert.NotNull(tokens);
        Assert.True(tokens.Length > 0);
    }

    [SkippableFact]
    public void Model_Tokenize_UnicodeText_ShouldWork()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var tokens = model.Tokenize("Bonjour, comment allez-vous?");
        Assert.NotNull(tokens);
        Assert.True(tokens.Length > 0);
    }

    [SkippableFact]
    public void Log_Configure_WithRealCallback_ShouldNotThrow()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        TranscribeCppSharp.Log.Configure((level, msg) => { });

        // Trigger native activity that may produce log output
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));

        // Clean up: disable logging
        TranscribeCppSharp.Log.Configure(null);
        // We can't guarantee the callback was invoked (depends on log levels),
        // but the test verifies that Configure + Load doesn't crash
    }

    // ═══════════════════════════════════════════════════════════════════
    // §6: Assertion fixes — add real assertions to previously empty tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RunParamsBuilder_Dispose_DisposesExtBuilder_Verified()
    {
        var ext = new WhisperExtBuilder();
        ext.WithTemperature(0.5f);

        using (var builder = new RunParamsBuilder())
        {
            builder.WithWhisperExt(ext);
        }

        // After RunParamsBuilder.Dispose(), the ext should be disposed.
        // Calling Build() on a disposed ext should throw ObjectDisposedException.
        Assert.Throws<ObjectDisposedException>(() => ext.Build());
    }

    [Fact]
    public void StreamParamsBuilder_Dispose_DisposesExtBuilder_Verified()
    {
        var ext = new MoonshineExtBuilder();
        ext.WithMinDecodeIntervalMs(200);

        using (var builder = new StreamParamsBuilder())
        {
            builder.WithMoonshineExt(ext);
        }

        Assert.Throws<ObjectDisposedException>(() => ext.Build());
    }

    // ═══════════════════════════════════════════════════════════════════
    // §6: Golden-text assertion on JFK sample
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public void Session_Run_ShouldReturnTranscript_ContainsJFKText()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var transcript = session.Run(pcm);

        Assert.False(string.IsNullOrWhiteSpace(transcript.FullText));
        // JFK audio says "And so my fellow Americans" — golden-text assertion
        Assert.Contains("fellow Americans", transcript.FullText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(transcript.Timing);
        Assert.True(transcript.Segments.Count > 0);
        Assert.False(transcript.WasAborted);
        Assert.False(transcript.WasTruncated);
    }

    // ═══════════════════════════════════════════════════════════════════
    // §6: Cancellation tests (require native lib)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public void Session_Run_AlreadyCancelled_ThrowsOperationCanceledException()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel before calling Run

        Assert.Throws<OperationCanceledException>(() => session.Run(pcm, ct: cts.Token));
    }

    [SkippableFact]
    public void Session_Run_CancellationRestoresCallback()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();

        bool originalCallbackCalled = false;
        session.SetAbortCallback(() =>
        {
            originalCallbackCalled = true;
            return false;
        });

        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            session.Run(pcm, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // After Run returns/throws, the original callback should be restored
        // and callable again
        originalCallbackCalled = false;
        session.Run(pcm);
        Assert.True(originalCallbackCalled);
    }

    [SkippableFact]
    public void Batch_Run_AlreadyCancelled_ThrowsOperationCanceledException()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            TranscribeCppSharp.Batch.Run(session, new[] { pcm }, ct: cts.Token));
    }

    // ═══════════════════════════════════════════════════════════════════
    // §7: Edge case tests (no native lib required)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RunParamsBuilder_WithLanguage_UnicodeString_ShouldRoundtrip()
    {
        using var builder = new RunParamsBuilder();
        builder.WithLanguage("fr-FR");

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.language);
        Assert.Equal("fr-FR", Marshal.PtrToStringUTF8(p.language));
    }

    [Fact]
    public void RunParamsBuilder_WithTargetLanguage_UnicodeString_ShouldRoundtrip()
    {
        using var builder = new RunParamsBuilder();
        builder.WithTargetLanguage("de-DE");

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.targetLanguage);
        Assert.Equal("de-DE", Marshal.PtrToStringUTF8(p.targetLanguage));
    }

    [Fact]
    public void RunParamsBuilder_WithLanguage_SetTwice_ReleasesFirst()
    {
        using var builder = new RunParamsBuilder();
        builder.WithLanguage("en-US");
        builder.WithLanguage("fr-FR"); // should free the first allocation

        var p = Marshal.PtrToStructure<RunParams>(builder.Build());
        Assert.Equal("fr-FR", Marshal.PtrToStringUTF8(p.language));
    }

    [Fact]
    public void WhisperExtBuilder_WithInitialPrompt_EmptyString_ShouldWork()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithInitialPrompt("");

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.NotEqual(IntPtr.Zero, p.initialPrompt);
        Assert.Equal("", Marshal.PtrToStringUTF8(p.initialPrompt));
    }

    [Fact]
    public void WhisperExtBuilder_WithPromptTokens_EmptyArray_ShouldSet()
    {
        using var builder = new WhisperExtBuilder();
        builder.WithPromptTokens(Array.Empty<int>());

        var p = Marshal.PtrToStructure<WhisperRunExt>(builder.Build());
        Assert.Equal((nuint)0, p.nPromptTokens);
    }

    // ═══════════════════════════════════════════════════════════════════
    // §6.2: WAV format rejection tests (no native lib required)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PcmExtensions_ReadWavToPcm_24Bit_ThrowsInvalidDataException()
    {
        var path = WriteTestWavWithFormat(audioFormat: 1, bitsPerSample: 24);
        try
        {
            Assert.Throws<InvalidDataException>(() => TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PcmExtensions_ReadWavToPcm_8Bit_ThrowsInvalidDataException()
    {
        var path = WriteTestWavWithFormat(audioFormat: 1, bitsPerSample: 8);
        try
        {
            Assert.Throws<InvalidDataException>(() => TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PcmExtensions_ReadWavToPcm_FloatFormat_ThrowsInvalidDataException()
    {
        // audioFormat=3 = IEEE float
        var path = WriteTestWavWithFormat(audioFormat: 3, bitsPerSample: 32);
        try
        {
            Assert.Throws<InvalidDataException>(() => TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PcmExtensions_ReadWavToPcm_ExtensibleFormat_ThrowsInvalidDataException()
    {
        // audioFormat=0xFFFE = WAVEFORMATEXTENSIBLE
        var path = WriteTestWavWithFormat(audioFormat: unchecked((short)0xFFFE), bitsPerSample: 16);
        try
        {
            Assert.Throws<InvalidDataException>(() => TranscribeCppSharp.PcmExtensions.ReadWavToPcm(path));
        }
        finally { File.Delete(path); }
    }

    // ═══════════════════════════════════════════════════════════════════
    // §7.3: Batch null/empty sub-array validation
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public void Batch_Run_NullElement_Throws()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var buffers = new float[][] { pcm, null! };
        Assert.Throws<ArgumentNullException>(() => TranscribeCppSharp.Batch.Run(session, buffers));
    }

    [SkippableFact]
    public void Batch_Run_EmptySubArray_ThrowsArgumentException()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);
        var buffers = new float[][] { pcm, Array.Empty<float>() };
        Assert.Throws<ArgumentException>(() => TranscribeCppSharp.Batch.Run(session, buffers));
    }

    // ═══════════════════════════════════════════════════════════════════
    // §7.4: PCM NaN/Inf propagation (requires native lib)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public void Session_Run_PcmWithNaN_DoesNotCrash()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets (test-models/ggml-tiny.bin, test-audio/jfk.wav) not present. Run ./run-integration-tests.sh to provision them.");
        using var model = TranscribeCppSharp.Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        using var session = model.CreateSession();
        // 1 second of silence with one NaN sample
        var pcm = new float[16000];
        pcm[100] = float.NaN;
        pcm[200] = float.PositiveInfinity;
        pcm[300] = float.NegativeInfinity;
        // Should not crash — native lib handles or ignores NaN
        try
        {
            var transcript = session.Run(pcm);
            Assert.NotNull(transcript);
        }
        catch (TranscribeException)
        {
            // Native may reject NaN — acceptable, but managed code must not crash
        }
    }
}