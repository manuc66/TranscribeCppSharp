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
/// The streaming tab only offers models that may stream.
/// </summary>
/// <remarks>
/// MOSS is the default model here and upstream records <c>streaming.supported =
/// false</c> for it, so the tab used to list it, let it be picked, and then refused
/// to start it — reaching the user as one uncopyable line of status text. The picker
/// now leaves out what upstream records as unable to stream (MOSS among them), and
/// Start is still gated on the loaded model's own answer for the rest, so a model
/// this machine cannot stream is not offered a run either. Both were asserted here
/// before this one change; the tests pin the new split: the picker reads upstream,
/// Start reads the machine.
/// </remarks>
[Collection("UiGlobalState")]
public class StreamingModelGuardTests
{
    private const string Moss = "moss-transcribe-diarize";

    /// <summary>An alias upstream records as streaming-capable.</summary>
    private const string Moonshine = "moonshine-streaming-tiny";

    private static string WithEmptyCache()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "tcsharp-stream-guard-" + Guid.NewGuid().ToString("N"));
        ModelStore.CacheRootOverride = root;
        return root;
    }

    private static ModelDescriptor Descriptor(string alias)
        => ModelStore.Catalog.Single(m => m.Alias == alias);

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

    /// <summary>
    /// MOSS is downloaded and upstream records it as unable to stream, so the
    /// picker leaves it out rather than offering a run that will be refused.
    /// </summary>
    [AvaloniaFact]
    public void AModelUpstreamSaysCannotStreamIsNotOffered()
    {
        InCache(() =>
        {
            Plant(Descriptor(Moss));
            var viewModel = new StreamingViewModel(
                new RecordingStreamingService(), new SettingsViewModel(),
                new ModelCapabilityCache { Probe = _ => Snapshot(streams: false) });
            Settle(viewModel);

            Assert.Empty(viewModel.AvailableModels);
            Assert.False(viewModel.HasStreamableModels);
            Assert.False(viewModel.CanStartStreaming);

            // It says the picker is empty without pretending nothing is on disk.
            Assert.Contains("No streaming model on disk", viewModel.ModelPickerHint, StringComparison.Ordinal);
            Assert.Contains("streaming", viewModel.ModelPickerHint, StringComparison.Ordinal);
        });
    }

    /// <summary>A streaming model is offered alongside it, and chosen.</summary>
    [AvaloniaFact]
    public void OnlyModelsThatMayStreamAreOffered()
    {
        InCache(() =>
        {
            Plant(Descriptor(Moss));
            Plant(Descriptor(Moonshine));
            var viewModel = new StreamingViewModel(
                new RecordingStreamingService(), new SettingsViewModel(),
                new ModelCapabilityCache { Probe = _ => Snapshot(streams: true) });
            Settle(viewModel);

            Assert.Equal(new[] { Moonshine }, viewModel.AvailableModels);
            Assert.Equal(Moonshine, viewModel.ModelAlias);
            Assert.Contains("1 of 2 on disk can stream", viewModel.ModelPickerHint, StringComparison.Ordinal);
            Assert.True(viewModel.CanStartStreaming);
        });
    }

    /// <summary>
    /// Upstream records the model as able to stream, but this machine's own answer
    /// is that it cannot; the picker keeps it (upstream's record is a real one) and
    /// Start is refused with a reason.
    /// </summary>
    [AvaloniaFact]
    public async Task AModelTheMachineSaysCannotStreamIsNotStarted()
    {
        RecordingStreamingService service = null!;
        StreamingViewModel viewModel = null!;

        InCache(() =>
        {
            Plant(Descriptor(Moonshine));
            service = new RecordingStreamingService();
            viewModel = new StreamingViewModel(
                service, new SettingsViewModel(),
                new ModelCapabilityCache { Probe = _ => Snapshot(streams: false) });
            Settle(viewModel);
        });

        Assert.Equal(new[] { Moonshine }, viewModel.AvailableModels);
        Assert.False(viewModel.SelectedModelStreams);
        Assert.False(viewModel.CanStartStreaming);
        Assert.Contains("does not support streaming", viewModel.StreamingSupportHint, StringComparison.Ordinal);

        await viewModel.StartStreamingCommand.ExecuteAsync(null);

        Assert.Equal(0, service.StreamCalls);
    }

    /// <summary>
    /// A model the machine confirms streams leaves Start enabled and the hint empty.
    /// </summary>
    [AvaloniaFact]
    public void AStreamingModelIsAllowed()
    {
        InCache(() =>
        {
            Plant(Descriptor(Moonshine));
            var viewModel = new StreamingViewModel(
                new RecordingStreamingService(), new SettingsViewModel(),
                new ModelCapabilityCache { Probe = _ => Snapshot(streams: true) });
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
