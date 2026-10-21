using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The memory probe: one window of the selected file, measured, before committing
/// to the whole run.
/// </summary>
[Collection("UiGlobalState")]
public class MemoryProbeTests
{
    private const string Alias = "parakeet-tdt-0.6b-v3";
    private const string DeviceName = "Fake GPU";

    private static async Task WithViewModel(
        ITranscriptionService service,
        Func<TranscriptionViewModel, Task> test)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-probe-" + Guid.NewGuid().ToString("N"));
        using var settingsFile = new UiSettingsScope();
        string audio = Path.Combine(
            Path.GetTempPath(), "tcsharp-probe-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(audio, string.Empty);

        // The tab only offers a run when a model is on disk; a placeholder file is
        // enough, because the fake service never loads it.
        ModelDescriptor descriptor = ModelStore.Catalog.Single(m => m.Alias == Alias);
        string cached = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        File.WriteAllText(cached, "placeholder");
        try
        {
            var viewModel = new TranscriptionViewModel(
                service, new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.AudioPath = audio;
            viewModel.ModelAlias = Alias;
            await test(viewModel);
        }
        finally
        {
            File.Delete(audio);
            ModelStore.CacheRootOverride = previous;
        }
    }

    private static MemoryObservation Observation(ulong minimumFree)
        => new(DeviceName, 4_294_967_296, minimumFree,
            new DateTimeOffset(2026, 10, 21, 1, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ASuccessfulProbeSaysItHoldsAndRemembersTheLowPoint()
    {
        var service = new ProbeService(new MemoryProbeResult(true, false, Observation(900_000_000), null));

        await WithViewModel(service, async viewModel =>
        {
            await viewModel.ProbeMemoryCommand.ExecuteAsync(null);

            Assert.False(viewModel.IsProbingMemory);
            Assert.Contains("Holds", viewModel.MemoryProbeStatus);
            Assert.Contains("measurement", viewModel.MemoryProbeStatus, StringComparison.OrdinalIgnoreCase);

            MemoryObservation? saved = UiSettings.LoadMemoryObservation(
                MemoryPreflight.RunKey(Alias, 300, Interop.BackendRequest.BackendAuto, DeviceName));
            Assert.NotNull(saved);
            Assert.Equal(900_000_000UL, saved.MinimumFreeBytes);
        });
    }

    [Fact]
    public async Task AProbeThatRunsOutOfMemoryNamesErrOomAndKeepsNoObservation()
    {
        var service = new ProbeService(new MemoryProbeResult(false, true, null, "out of memory"));

        await WithViewModel(service, async viewModel =>
        {
            await viewModel.ProbeMemoryCommand.ExecuteAsync(null);

            Assert.False(viewModel.IsProbingMemory);
            Assert.Contains("ErrOom", viewModel.MemoryProbeStatus);
            Assert.Contains("Window", viewModel.MemoryProbeStatus);
            Assert.Null(UiSettings.LoadMemoryObservation(
                MemoryPreflight.RunKey(Alias, 300, Interop.BackendRequest.BackendAuto, DeviceName)));
        });
    }

    [Fact]
    public async Task TheProbeIsDisabledWhileItRuns()
    {
        await WithViewModel(new ProbeService(new MemoryProbeResult(true, false, null, null)), async viewModel =>
        {
            // The constructor starts a capability probe; the placeholder model fails
            // to load, and the flag clears shortly after.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (viewModel.IsCheckingCapabilities && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.True(viewModel.CanProbeMemory);

            viewModel.IsProbingMemory = true;
            Assert.False(viewModel.CanProbeMemory);

            viewModel.IsProbingMemory = false;
            Assert.True(viewModel.CanProbeMemory);
        });
    }

    /// <summary>Returns whatever the test asks the probe to return.</summary>
    private sealed class ProbeService(MemoryProbeResult result) : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(result);

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, IProgress<BatchFileStep>? fileStep = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
