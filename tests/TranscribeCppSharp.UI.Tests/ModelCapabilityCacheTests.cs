using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.Services;
using Xunit;

// Interop declares its own Task; the async one is System.Threading.Tasks.Task.
using Task = System.Threading.Tasks.Task;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// One model, one load, however many tabs ask for it at once.
/// </summary>
/// <remarks>
/// The startup OOM on a machine that was already short of memory came from two
/// view models each loading the same 700 MB file on its own thread. The cache is
/// what makes the second caller wait for the first, so these tests pin the
/// sharing rather than a snapshot's contents. <see cref="ModelCapabilityCache.Probe"/>
/// is the seam that lets them run without loading a model.
/// </remarks>
public class ModelCapabilityCacheTests
{
    private static ModelCapabilitySnapshot Snapshot(string architecture = "whisper")
        => new(
            Architecture: architecture,
            Variant: "test",
            Roles: TranscribeRole.Asr,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
            SupportsSpecDecode: false,
            Features: new HashSet<Feature>(),
            Languages: [],
            TranslateTargetLanguages: []);

    /// <summary>
    /// Two callers for one alias share a single load, even while it is still
    /// running.
    /// </summary>
    [Fact]
    public async Task TwoCallersForOneAliasLoadOnce()
    {
        int loads = 0;
        using var gate = new ManualResetEventSlim(false);

        var cache = new ModelCapabilityCache
        {
            Probe = _ =>
            {
                Interlocked.Increment(ref loads);
                gate.Wait(TimeSpan.FromSeconds(10));
                return Snapshot();
            },
        };

        // Both in flight at once, as the two view models are at startup.
        Task<ModelCapabilitySnapshot> first = cache.GetAsync("moss-transcribe-diarize", "/tmp/a.gguf");
        Task<ModelCapabilitySnapshot> second = cache.GetAsync("moss-transcribe-diarize", "/tmp/a.gguf");
        gate.Set();

        ModelCapabilitySnapshot[] results = await Task.WhenAll(first, second);

        Assert.Equal(1, Volatile.Read(ref loads));
        Assert.Same(results[0], results[1]);
    }

    /// <summary>A finished answer is handed back without loading again.</summary>
    [Fact]
    public async Task AFinishedAnswerIsReused()
    {
        int loads = 0;
        var cache = new ModelCapabilityCache
        {
            Probe = _ =>
            {
                Interlocked.Increment(ref loads);
                return Snapshot();
            },
        };

        ModelCapabilitySnapshot first = await cache.GetAsync("whisper-tiny", "/tmp/b.gguf");
        Assert.True(cache.TryGet("whisper-tiny", out ModelCapabilitySnapshot? known));
        Assert.Same(first, known);

        ModelCapabilitySnapshot second = await cache.GetAsync("whisper-tiny", "/tmp/b.gguf");
        Assert.Equal(1, Volatile.Read(ref loads));
        Assert.Same(first, second);
    }

    /// <summary>
    /// A failed load is not an answer: the next caller tries again rather than
    /// being handed the same exception forever.
    /// </summary>
    [Fact]
    public async Task AFailedLoadIsNotRemembered()
    {
        int loads = 0;
        var cache = new ModelCapabilityCache
        {
            Probe = _ =>
            {
                if (Interlocked.Increment(ref loads) == 1)
                {
                    throw new InvalidOperationException("cannot load");
                }

                return Snapshot();
            },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetAsync("whisper-tiny", "/tmp/c.gguf"));

        Assert.False(cache.TryGet("whisper-tiny", out _));

        ModelCapabilitySnapshot second = await cache.GetAsync("whisper-tiny", "/tmp/c.gguf");
        Assert.Equal(2, Volatile.Read(ref loads));
        Assert.Equal("whisper", second.Architecture);
    }
}
