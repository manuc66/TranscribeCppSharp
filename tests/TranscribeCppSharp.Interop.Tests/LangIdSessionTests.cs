using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Language identification against the real VoxLingua107 ECAPA-TDNN weights
/// (Q8_0, 27 MB, fetched by scripts/run-integration-tests.sh). The model serves
/// TRANSCRIBE_ROLE_LANGID, so <see cref="LangIdSession"/> runs against the real
/// native library.
/// </summary>
public class LangIdSessionTests
{
    private static string LangIdPath => Path.Combine(TestConfig.RepoRoot, "test-models", "lang-id-voxlingua107-ecapa-Q8_0.gguf");

    private static bool Ready => File.Exists(LangIdPath) && File.Exists(TestConfig.AudioPath);

    private static float[] Pcm() => PcmExtensions.ReadWavToPcm(TestConfig.AudioPath);

    // CA1861: a constant array is built once for the class, not on every call.
    private static readonly string[] AllowedCodes = ["en", "fr", "de"];

    private static Model LoadLangId()
        => Model.Load(LangIdPath, p => p.WithBackend(BackendRequest.BackendCpu));

    [SkippableFact]
    public void TheModelServesTheLangidRole_AndReportsItsLabels()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();

        Assert.True(model.Roles.HasFlag(TranscribeRole.Langid));
        Assert.False(model.Roles.HasFlag(TranscribeRole.Asr));

        var info = model.GetLangIdInfo();
        Assert.Equal(16000, info.SampleRate);
        Assert.Equal(107, info.LabelCount);
        Assert.True(info.MaxAudioMs > 0);
    }

    [SkippableFact]
    public void LabelAccessors_MapCodesNamesAndIndices()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();

        string? firstCode = model.GetLangIdLabelCode(0);
        Assert.False(string.IsNullOrEmpty(firstCode));
        Assert.False(string.IsNullOrEmpty(model.GetLangIdLabelName(0)));

        int en = model.GetLangIdLabelIndex("en");
        Assert.InRange(en, 0, 106);
        Assert.Equal("en", model.GetLangIdLabelCode(en));

        // An out-of-range index and an unknown code are answered, not thrown.
        Assert.Null(model.GetLangIdLabelCode(10_000));
        Assert.True(model.GetLangIdLabelIndex("not-a-language") < 0);
    }

    [SkippableFact]
    public void Run_IdentifiesEnglishInTheSample()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession();

        var result = langid.Run(Pcm());

        Assert.NotEmpty(result.Candidates);
        Assert.Equal("en", result.Candidates[0].Code);
        // Unrestricted: the candidates cover the whole softmax.
        Assert.Equal(1f, result.AllowedMass, 3);
        Assert.All(result.Candidates, c => Assert.InRange(c.Probability, 0f, 1f));
    }

    [SkippableFact]
    public void Run_RestrictedToAnAllowedSet_RanksOnlyThose()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession();

        var result = langid.Run(Pcm(), p => p.WithAllowed(AllowedCodes));

        Assert.Equal(3, result.Candidates.Count);
        Assert.Contains(result.Candidates, c => c.Code == "en");
        Assert.All(result.Candidates, c => Assert.Contains(c.Code, AllowedCodes));
        // The allowed mass is a share, and restricting cannot exceed 1.
        Assert.InRange(result.AllowedMass, 0f, 1f);
    }

    [SkippableFact]
    public void Run_WithAThreadCount_Works()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession(p => p.WithThreads(2));

        Assert.NotEmpty(langid.Run(Pcm()).Candidates);
    }

    [SkippableFact]
    public void GetTimings_ReturnsStatistics()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession();
        langid.Run(Pcm());

        Assert.NotNull(langid.GetTimings());
    }

    [SkippableFact]
    public void Run_AlreadyCancelled_ThrowsBeforeRunning()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => langid.Run(Pcm(), ct: cts.Token));
    }

    [SkippableFact]
    public void Run_WithAnUncancelledToken_InstallsAndClearsTheAbortSource()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession();

        using var cts = new CancellationTokenSource();
        Assert.NotEmpty(langid.Run(Pcm(), ct: cts.Token).Candidates);
    }

    [SkippableFact]
    public void Dispose_IsIdempotent_AndUseAfterDisposeThrows()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        var langid = model.CreateLangIdSession();
        langid.Dispose();
        langid.Dispose();

        Assert.Throws<ObjectDisposedException>(() => langid.Run(Pcm()));
    }

    [SkippableFact]
    public void Run_NonFiniteAudio_IsRejected()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession();

        var pcm = new float[16000];
        pcm[100] = float.NaN;

        var ex = Assert.Throws<TranscribeException>(() => langid.Run(pcm));
        Assert.Equal(Status.ErrInvalidArg, ex.StatusCode);
    }

    [SkippableFact]
    public void ReadResult_BeforeAnyRun_HasNoCandidates()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession();

        var result = langid.ReadResult();
        Assert.Empty(result.Candidates);
    }

    [SkippableFact]
    public void Run_AbortedMidRun_ThrowsOperationCanceledException()
    {
        Skip.IfNot(Ready, "LANGID model or audio not present.");

        using var model = LoadLangId();
        using var langid = model.CreateLangIdSession();

        // Cancel from the configure callback: Run invokes it after its
        // pre-cancelled check and before the native call, so the token is already
        // cancelled when the native library first polls it and the run aborts.
        // That abort is translated to OperationCanceledException, which is what
        // this test guards (the pre-cancelled case is a separate test).
        //
        // Cancel from a timer or a background thread instead and it flakes: langid
        // polls the abort callback only in the first few tens of milliseconds of
        // the run and then does the bulk (~0.6 s) without checking, so a
        // cancellation that lands even slightly late is never seen and the run
        // completes normally. It needs no long input this way either.
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(
            () => langid.Run(Pcm(), configure: _ => cts.Cancel(), ct: cts.Token));
    }
}
