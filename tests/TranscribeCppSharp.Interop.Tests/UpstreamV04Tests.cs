#nullable enable

using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The surface added by upstream transcribe.cpp 0.4: roles, the diarize/langid/
/// vad session builders, and the backend allowlist. Behaviour that needs a model
/// of the matching role (Sortformer, Silero VAD, VoxLingua) is not exercised here
/// — those weights are not part of the test assets — but the enum values, the
/// parameter marshalling and the role/capability contract against a real ASR
/// model are.
/// </summary>
public class UpstreamV04Tests
{
    private static bool IsIntegrationEnv => TestConfig.IsIntegrationTestEnvironment();

    [Fact]
    public void TranscribeRole_MirrorsTheNativeEnum()
    {
        // The managed flags must equal the native bit values, or Model.Roles
        // would report the wrong work.
        Assert.Equal((TranscribeRole)Role.RoleAsr, TranscribeRole.Asr);
        Assert.Equal((TranscribeRole)Role.RoleDiarize, TranscribeRole.Diarize);
        Assert.Equal((TranscribeRole)Role.RoleLangid, TranscribeRole.Langid);
        Assert.Equal((TranscribeRole)Role.RoleVad, TranscribeRole.Vad);
    }

    [Fact]
    public void BackendMask_MirrorsTheUpstreamConstants()
    {
        Assert.Equal(0x00000001u, (uint)BackendMask.Cpu);
        Assert.Equal(0x00000002u, (uint)BackendMask.Metal);
        Assert.Equal(0x00000004u, (uint)BackendMask.Vulkan);
        Assert.Equal(0x00000008u, (uint)BackendMask.Cuda);
        Assert.Equal(0x00000010u, (uint)BackendMask.Rocm);
        Assert.Equal(0x80000000u, (uint)BackendMask.Other);
        Assert.Equal(0xFFFFFFFFu, (uint)BackendMask.All);
    }

    [Fact]
    public void NewStatusCodes_CarryANativeMessage()
    {
        // Status 20/21 are new in 0.4; the wrapper maps them through the native
        // status string, so the exception must name them rather than fall back
        // to a bare number.
        var role = new TranscribeException(Status.ErrUnsupportedRole);
        Assert.Equal(20, role.ErrorCode);
        Assert.Contains("(20)", role.Message, StringComparison.Ordinal);

        var shortAudio = new TranscribeException(Status.ErrInputTooShort);
        Assert.Equal(21, shortAudio.ErrorCode);
        Assert.Contains("(21)", shortAudio.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiarizeSessionParamsBuilder_SetsThreads()
    {
        using var builder = new DiarizeSessionParamsBuilder();
        builder.WithThreads(3);

        var p = Marshal.PtrToStructure<DiarizeSessionParams>(builder.Build());
        Assert.Equal(3, p.nThreads);
    }

    [Fact]
    public void LangIdParamsBuilder_WithAllowed_MarshalsThePointerArray()
    {
        using var builder = new LangIdParamsBuilder();
        builder.WithAllowed(new[] { "en", "fr", "de" });

        var p = Marshal.PtrToStructure<LangidParams>(builder.Build());
        Assert.Equal(3, p.nAllowed);
        Assert.NotEqual(IntPtr.Zero, p.allowed);

        var first = Marshal.ReadIntPtr(p.allowed, 0);
        Assert.Equal("en", Marshal.PtrToStringUTF8(first));
    }

    [Fact]
    public void LangIdParamsBuilder_WithNoAllowed_LeavesTheArrayEmpty()
    {
        using var builder = new LangIdParamsBuilder();

        var p = Marshal.PtrToStructure<LangidParams>(builder.Build());
        Assert.Equal(0, p.nAllowed);
        Assert.Equal(IntPtr.Zero, p.allowed);
    }

    [Fact]
    public void VadParamsBuilder_SetsEveryKnob()
    {
        using var builder = new VadParamsBuilder();
        builder.WithThreshold(0.7)
            .WithNegThreshold(0.4)
            .WithMinSpeechMs(300)
            .WithMinSilenceMs(120)
            .WithSpeechPadMs(40)
            .WithMaxSpeechMs(15000);

        var p = Marshal.PtrToStructure<VadParams>(builder.Build());
        Assert.Equal(0.7, p.threshold);
        Assert.Equal(0.4, p.negThreshold);
        Assert.Equal(300, p.minSpeechMs);
        Assert.Equal(120, p.minSilenceMs);
        Assert.Equal(40, p.speechPadMs);
        Assert.Equal(15000, p.maxSpeechMs);
    }

    [Fact]
    public void VadSessionParamsBuilder_SetsThreads()
    {
        using var builder = new VadSessionParamsBuilder();
        builder.WithThreads(2);

        var p = Marshal.PtrToStructure<VadSessionParams>(builder.Build());
        Assert.Equal(2, p.nThreads);
    }

    [Fact]
    public void Backends_AllowedBackends_IsReadable()
    {
        // Reading the effective mask must not require initialization and must not
        // throw; the value is the library default before InitDefault.
        BackendMask mask = Backends.AllowedBackends;
        Assert.NotEqual(BackendMask.None, mask);
    }

    [SkippableFact]
    public void Whisper_ServesAsr_AndRefusesTheOtherRoles()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets not present.");

        using var model = Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));

        Assert.True(model.Roles.HasFlag(TranscribeRole.Asr));
        Assert.False(model.Roles.HasFlag(TranscribeRole.Diarize));

        // 0.4 makes capabilities ASR-only and routes the other queries through
        // the role check: each must fail with UNSUPPORTED_ROLE, not a zeroed
        // struct.
        foreach (Func<object> call in new Func<object>[]
        {
            () => model.GetDiarizeInfo(),
            () => model.GetLangIdInfo(),
            () => model.GetVadInfo(),
        })
        {
            var ex = Assert.Throws<TranscribeException>(call);
            Assert.Equal(Status.ErrUnsupportedRole, ex.StatusCode);
        }
    }

    [SkippableFact]
    public void Whisper_CreateDiarizeSession_FailsWithUnsupportedRole()
    {
        Skip.IfNot(IsIntegrationEnv, "Integration test assets not present.");

        using var model = Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));

        var ex = Assert.Throws<TranscribeException>(() => model.CreateDiarizeSession());
        Assert.Equal(Status.ErrUnsupportedRole, ex.StatusCode);
    }
}
