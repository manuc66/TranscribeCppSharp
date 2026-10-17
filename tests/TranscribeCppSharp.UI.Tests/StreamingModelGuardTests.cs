using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

// Interop has its own Task and StreamUpdate; the ones this file means are these.
using Task = System.Threading.Tasks.Task;
using StreamUpdate = TranscribeCppSharp.UI.Models.StreamUpdate;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The streaming tab does not start a model that cannot stream.
/// </summary>
/// <remarks>
/// MOSS is the default model here and upstream records <c>streaming.supported =
/// false</c> for it, so the tab used to offer Start for a model the native library
/// refuses with <c>ErrNotImplemented</c> — an error that reached the user as one
/// uncopyable line of status text. These tests pin the gate, on the loaded model's
/// answer and on the pinned catalog's, and that Start never reaches the service.
/// </remarks>
public class StreamingModelGuardTests
{
    private const string Moss = "moss-transcribe-diarize";

    private static string WithEmptyCache()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "tcsharp-stream-guard-" + Guid.NewGuid().ToString("N"));
        ModelStore.CacheRootOverride = root;
        return root;
    }

    private static void Plant(ModelDescriptor descriptor)
    {
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
    }

    private static ModelCapabilitySnapshot Snapshot(bool streams)
        => new(
            Architecture: streams ? "moonshine_streaming" : "moss",
            Variant: "test",
            Roles: TranscribeRole.Asr,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: streams,
            SupportsSpecDecode: false,
            Features: new HashSet<Feature>(),
            Languages: [],
            TranslateTargetLanguages: []);

    private static void Settle(StreamingViewModel viewModel)
    {
        const int quietMs = 300;
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        DateTime quietSince = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();

            if (viewModel.IsCheckingCapabilities)
            {
                quietSince = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - quietSince).TotalMilliseconds >= quietMs)
            {
                Dispatcher.UIThread.RunJobs();
                Assert.False(viewModel.IsCheckingCapabilities,
                    $"the capability probe never settled; status is '{viewModel.CapabilityStatus}'");
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail("no quiet period in 30 s");
    }

    /// <summary>Runs the body with a temporary cache root, then restores it.</summary>
    private static void InCache(Action body)
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            WithEmptyCache();
            body();
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    [AvaloniaFact]
    public void AModelThatCannotStreamDisablesStartAndSaysWhy()
    {
        InCache(() =>
        {
            Plant(ModelStore.Catalog.Single(m => m.Alias == Moss));
            var cache = new ModelCapabilityCache { Probe = _ => Snapshot(streams: false) };
            var viewModel = new StreamingViewModel(
                new RecordingStreamingService(), new SettingsViewModel(), cache);
            Settle(viewModel);

            Assert.False(viewModel.CanStartStreaming);
            Assert.False(viewModel.SelectedModelStreams);
            Assert.Contains("does not support streaming", viewModel.StreamingSupportHint,
                StringComparison.Ordinal);
            Assert.Contains(Moss, viewModel.StreamingSupportHint, StringComparison.Ordinal);
        });
    }

    [AvaloniaFact]
    public async Task StartOnSuchAModelNeverReachesTheService()
    {
        RecordingStreamingService service = null!;
        StreamingViewModel viewModel = null!;

        InCache(() =>
        {
            Plant(ModelStore.Catalog.Single(m => m.Alias == Moss));
            var cache = new ModelCapabilityCache { Probe = _ => Snapshot(streams: false) };
            service = new RecordingStreamingService();
            viewModel = new StreamingViewModel(service, new SettingsViewModel(), cache);
            Settle(viewModel);
        });

        await viewModel.StartStreamingCommand.ExecuteAsync(null);

        Assert.Equal(0, service.StreamCalls);
        Assert.Contains("does not support streaming", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no probe answer, upstream's catalog is what says no — the same source
    /// the Models tab filters on.
    /// </summary>
    [AvaloniaFact]
    public void UpstreamCatalogBlocksAModelWhoseProbeFailed()
    {
        InCache(() =>
        {
            Plant(ModelStore.Catalog.Single(m => m.Alias == Moss));
            var cache = new ModelCapabilityCache
            {
                Probe = _ => throw new InvalidOperationException("cannot load"),
            };
            var viewModel = new StreamingViewModel(
                new RecordingStreamingService(), new SettingsViewModel(), cache);
            Settle(viewModel);

            // The probe failed, so the loaded model has no answer; upstream does.
            Assert.False(viewModel.CanStartStreaming);
            Assert.Contains("does not support streaming", viewModel.StreamingSupportHint,
                StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A model that streams is not blocked by upstream's "false": the loaded model
    /// is the authoritative answer once it has been asked.
    /// </summary>
    [AvaloniaFact]
    public void TheLoadedModelOverridesUpstreamWhenItStreams()
    {
        InCache(() =>
        {
            Plant(ModelStore.Catalog.Single(m => m.Alias == Moss));
            var cache = new ModelCapabilityCache { Probe = _ => Snapshot(streams: true) };
            var viewModel = new StreamingViewModel(
                new RecordingStreamingService(), new SettingsViewModel(), cache);
            Settle(viewModel);

            Assert.True(viewModel.SelectedModelStreams);
            Assert.True(viewModel.CanStartStreaming);
            Assert.Empty(viewModel.StreamingSupportHint);
        });
    }

    /// <summary>Counts attempts, so a refused Start can be shown not to have run.</summary>
    private sealed class RecordingStreamingService : ITranscriptionService
    {
        public int StreamCalls;

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref StreamCalls);
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
