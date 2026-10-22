using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

using Task = System.Threading.Tasks.Task;
using StreamUpdate = TranscribeCppSharp.UI.Models.StreamUpdate;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// What happens to the Window setting after a run: a lowered value is reported,
/// remembered for the model/backend/device combination, and applied again when
/// that combination comes back.
/// </summary>
[Collection("UiGlobalState")]
public class WindowPersistenceTests
{
    private const string Alias = "parakeet-tdt-0.6b-v3";
    private const string DeviceName = "Fake GPU";

    private static async Task WithViewModel(
        ITranscriptionService service,
        Func<SettingsViewModel, TranscriptionViewModel, Task> test,
        Action<SettingsViewModel>? before = null)
    {
        string? previousCache = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-window-" + Guid.NewGuid().ToString("N"));
        using var settingsFile = new UiSettingsScope();
        string audio = Path.Combine(
            Path.GetTempPath(), "tcsharp-window-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(audio, string.Empty);

        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var settings = new SettingsViewModel();
            settings.Devices.Clear();
            settings.SelectedBackend = BackendRequest.BackendVulkan;
            settings.SelectedDeviceOption = new DeviceOption(new BackendDevice(
                DeviceName, DeviceName, "vulkan", "fake", 4_294_967_296, 3_000_000_000,
                DeviceType.DeviceTypeGpu));

            before?.Invoke(settings);

            var viewModel = new TranscriptionViewModel(
                service, settings, new ModelCapabilityCache());
            viewModel.AudioPath = audio;
            await test(settings, viewModel);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            File.Delete(audio);
            ModelStore.CacheRootOverride = previousCache;
        }
    }

    [Fact]
    public async Task ALoweredWindowIsReportedAndRemembered()
    {
        var result = new TranscriptionResult
        {
            Roles = TranscribeRole.Asr,
            FullText = "text",
            EffectiveWindowSeconds = 15,
            RequestedWindowSeconds = 30,
            Memory = new MemoryObservation(
                DeviceName, 4_294_967_296, 1_000_000_000,
                new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero)),
        };
        TranscriptionViewModel? running = null;
        var service = new FixedService(result)
        {
            // The note lives only while the run is in flight; the finally block
            // clears it with the rest of the progress state.
            StepReported = () => Assert.Contains("lowered to 15 s", running!.ProgressNote),
        };

        await WithViewModel(service, async (settings, viewModel) =>
        {
            running = viewModel;
            viewModel.ModelAlias = Alias;
            viewModel.WindowSeconds = 30;

            await viewModel.TranscribeCommand.ExecuteAsync(null);

            Assert.Equal(15, viewModel.WindowSeconds);
            Assert.Contains("lowered", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("15 s", viewModel.StatusMessage);

            // The value follows the model, backend and device that used it.
            Assert.Equal(15, UiSettings.LoadWindow(
                MemoryPreflight.CombinationKey(
                    Alias, settings.SelectedBackend, DeviceName)));
        });
    }

    [Fact]
    public async Task ARememberedWindowIsAppliedToItsCombination()
    {
        await WithViewModel(
            new FixedService(new TranscriptionResult { Roles = TranscribeRole.Asr }),
            before: settings => UiSettings.SaveWindow(
                MemoryPreflight.CombinationKey(
                    Alias, settings.SelectedBackend, DeviceName),
                75),
            test: (_, viewModel) =>
            {
                viewModel.ModelAlias = Alias;

                Assert.Equal(75, viewModel.WindowSeconds);
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task TheAutoAdjustChoiceIsRemembered()
    {
        await WithViewModel(
            new FixedService(new TranscriptionResult { Roles = TranscribeRole.Asr }),
            test: (_, viewModel) =>
            {
                Assert.True(viewModel.AutoAdjustWindow);

                viewModel.AutoAdjustWindow = false;

                Assert.False(UiSettings.LoadAutoAdjustWindow());
                return Task.CompletedTask;
            });
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
            => callback(state);
    }

    /// <summary>Returns one prepared result; it does not load a model or read audio.</summary>
    private sealed class FixedService(TranscriptionResult result) : ITranscriptionService
    {
        /// <summary>Runs after the step is posted, while the run is still in flight.</summary>
        public Action? StepReported { get; init; }

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            string modelPath,
            TranscriptionOptions options,
            IProgress<double>? progress = null,
            IProgress<string>? liveText = null,
            IProgress<TranscriptionPhase>? phase = null,
            IProgress<TranscriptionStep>? step = null,
            CancellationToken cancellationToken = default)
        {
            if (result.EffectiveWindowSeconds is int window)
            {
                step?.Report(new TranscriptionStep(
                    TranscriptionPhase.Transcribing, 0, 4, window));
                StepReported?.Invoke();
            }

            return Task.FromResult(result);
        }

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath,
            StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath,
            string modelPath,
            TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths,
            string modelPath,
            TranscriptionOptions options,
            IProgress<int>? progress = null,
            IProgress<BatchFileStep>? fileStep = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

/// <summary>The halving policy used when a window does not fit.</summary>
public class WindowAdjustmentTests
{
    [Theory]
    [InlineData(300, 150)]
    [InlineData(150, 75)]
    [InlineData(30, 15)]
    [InlineData(11, 5)]
    [InlineData(10, 5)]
    public void AWindowIsHalved(int requested, int expected)
        => Assert.Equal(expected, WindowAdjustment.Smaller(requested));

    [Theory]
    [InlineData(5)]
    [InlineData(2)]
    public void TheFloorEndsTheRetries(int requested)
        => Assert.Null(WindowAdjustment.Smaller(requested));
}
