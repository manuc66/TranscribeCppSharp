using Avalonia.Headless.XUnit;
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
/// The app remembers the model each tab last ran, and reselects it.
/// </summary>
/// <remarks>
/// The file that holds the note, the rule that picks a selection from it, and the
/// two view models that read it are all asserted here: a change that stops
/// recording a run, or stops honouring the note, shows up as a failure rather
/// than as a picker that quietly went back to the first alias.
/// </remarks>
[Collection("UiGlobalState")]
public class LastUsedModelTests
{
    /// <summary>
    /// Runs the body with a temporary cache root, so the aliases on disk are the
    /// ones the test plants, then restores it. The settings file is redirected
    /// separately, by <see cref="UiSettingsScope"/>.
    /// </summary>
    private static void InCache(Action body)
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            body();
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    private static void Plant(ModelDescriptor descriptor)
    {
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
    }

    private static ModelCapabilitySnapshot AnySnapshot()
        => new(
            Architecture: "any",
            Variant: "test",
            Roles: TranscribeRole.Asr,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: true,
            SupportsSpecDecode: false,
            Features: new HashSet<Feature>(),
            Languages: [],
            TranslateTargetLanguages: []);

    // ---- the file ---------------------------------------------------------

    [Fact]
    public void RemembersEachTabSeparately()
    {
        using var settings = new UiSettingsScope();

        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");
        UiSettings.SetLastModel(UiSettings.StreamingTab, "moonshine-streaming-tiny");

        Assert.Equal("whisper-small", UiSettings.LastModel(UiSettings.TranscriptionTab));
        Assert.Equal("moonshine-streaming-tiny", UiSettings.LastModel(UiSettings.StreamingTab));
        Assert.Null(UiSettings.LastModel(UiSettings.BatchTab));
    }

    [Fact]
    public void AMissingFileIsNoAnswer()
    {
        using var settings = new UiSettingsScope();
        Assert.Null(UiSettings.LastModel(UiSettings.TranscriptionTab));
    }

    [Fact]
    public void ADamagedFileIsNoAnswerAndIsOverwritten()
    {
        using var settings = new UiSettingsScope();
        Directory.CreateDirectory(Path.GetDirectoryName(UiSettings.FilePath)!);
        File.WriteAllText(UiSettings.FilePath, "{ not json");

        Assert.Null(UiSettings.LastModel(UiSettings.TranscriptionTab));

        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");
        Assert.Equal("whisper-small", UiSettings.LastModel(UiSettings.TranscriptionTab));
    }

    [Fact]
    public void AnEmptyAliasIsNotRecorded()
    {
        using var settings = new UiSettingsScope();
        UiSettings.SetLastModel(UiSettings.TranscriptionTab, "whisper-small");
        UiSettings.SetLastModel(UiSettings.TranscriptionTab, string.Empty);

        Assert.Equal("whisper-small", UiSettings.LastModel(UiSettings.TranscriptionTab));
    }

    // ---- the rule ---------------------------------------------------------

    [Fact]
    public void SelectionKeepsTheCurrentChoiceWhenItIsStillOffered()
    {
        Assert.Equal("b", DownloadedModelCatalog.Selection("b", ["a", "b", "c"], "c"));
    }

    [Fact]
    public void SelectionFallsBackToTheLastRunModel()
    {
        Assert.Equal("c", DownloadedModelCatalog.Selection("gone", ["a", "b", "c"], "c"));
    }

    [Fact]
    public void SelectionFallsBackToTheFirstModel()
    {
        Assert.Equal("a", DownloadedModelCatalog.Selection("gone", ["a", "b", "c"], "also-gone"));
        Assert.Equal("a", DownloadedModelCatalog.Selection(string.Empty, ["a", "b"], null));
    }

    [Fact]
    public void SelectionIsEmptyWhenNothingIsOffered()
    {
        Assert.Equal(string.Empty, DownloadedModelCatalog.Selection("a", [], "b"));
    }

    // ---- the view models --------------------------------------------------

    [AvaloniaFact]
    public void TranscriptionReselectsTheModelLastRun()
    {
        using var settings = new UiSettingsScope();

        InCache(() =>
        {
            ModelDescriptor first = ModelStore.Catalog[0];
            ModelDescriptor second = ModelStore.Catalog[1];
            Plant(first);
            Plant(second);

            UiSettings.SetLastModel(UiSettings.TranscriptionTab, second.Alias);

            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(),
                new ModelCapabilityCache { Probe = _ => AnySnapshot() });

            Assert.Equal(second.Alias, viewModel.ModelAlias);
        });
    }

    [AvaloniaFact]
    public void ARememberedModelNoLongerOnDiskFallsBackToTheFirst()
    {
        using var settings = new UiSettingsScope();

        InCache(() =>
        {
            ModelDescriptor first = ModelStore.Catalog[0];
            Plant(first);
            UiSettings.SetLastModel(UiSettings.TranscriptionTab, "deleted-model");

            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(),
                new ModelCapabilityCache { Probe = _ => AnySnapshot() });

            Assert.Equal(first.Alias, viewModel.ModelAlias);
        });
    }

    [AvaloniaFact]
    public async Task BatchRecordsTheModelARunStartedWith()
    {
        using var settings = new UiSettingsScope();

        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            ModelDescriptor only = ModelStore.Catalog[0];
            Plant(only);

            var viewModel = new BatchViewModel(new EmptyBatchService(), new SettingsViewModel());
            viewModel.AudioFiles.Add("/tmp/an-audio-file.wav");

            Assert.Equal(only.Alias, viewModel.ModelAlias);
            Assert.Null(UiSettings.LastModel(UiSettings.BatchTab));

            await viewModel.ProcessBatchCommand.ExecuteAsync(null);

            Assert.Equal(only.Alias, UiSettings.LastModel(UiSettings.BatchTab));
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    /// <summary>A batch over nothing: enough to reach the record call, no audio read.</summary>
    private sealed class EmptyBatchService : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            IProgress<double>? progress = null, IProgress<string>? liveText = null, IProgress<TranscriptionPhase>? phase = null, IProgress<TranscriptionStep>? step = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamUpdate> StreamTranscribeAsync(
            string modelPath, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<MemoryProbeResult> ProbeMemoryAsync(
            string audioPath, string modelPath, TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<BatchItemResult>> BatchTranscribeAsync(
            IReadOnlyList<string> audioPaths, string modelPath, TranscriptionOptions options,
            IProgress<int>? progress = null, IProgress<BatchFileStep>? fileStep = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<BatchItemResult>());
    }
}
