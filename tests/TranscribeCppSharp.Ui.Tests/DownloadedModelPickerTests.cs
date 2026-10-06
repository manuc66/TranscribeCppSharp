using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The model pickers offer what is on disk, and say where to get the rest.
/// </summary>
/// <remarks>
/// The point of the filtering is that a picker entry means "you can run this now".
/// The point of the hint and the button is that it does not hide the other 70-odd
/// models: the way to more of them is on screen in all three tabs, and coming back
/// from the Models tab has to show what was just downloaded — otherwise the guidance
/// points at a download that never appears.
/// </remarks>
public class DownloadedModelPickerTests
{
    /// <summary>
    /// Unique cache root per test, so one test's planted model is not another's.
    /// </summary>
    private static string WithEmptyCache()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-" + Guid.NewGuid().ToString("N"));
        ModelStore.CacheRootOverride = root;
        return root;
    }

    private static void Plant(ModelDescriptor descriptor)
    {
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Contents do not matter: IsCached is a File.Exists, and these tests never
        // transcribe. The probe may read it and report a failure, which is the
        // same outcome as any other model it cannot check.
        File.WriteAllText(path, "placeholder");
    }

    private static TranscriptionViewModel Transcription()
        => new(new StubTranscriptionService(), new SettingsViewModel());

    /// <summary>
    /// Waits for the startup capability probe to finish, which is what gates
    /// <see cref="TranscriptionViewModel.CanTranscribe"/>.
    /// </summary>
    /// <remarks>
    /// A model found on disk is loaded to be asked what it can do, and the button
    /// stays disabled until that answers. Asserting without waiting would be testing
    /// the timing of a background load rather than the behaviour. Bounded so a probe
    /// that never comes back fails the assertion instead of hanging the run.
    /// </remarks>
    private static void Settle(TranscriptionViewModel viewModel)
    {
        // Two probes, not one: choosing the alias in LoadModels fires
        // OnModelAliasChanged, and the constructor then asks for the same alias,
        // which is queued because a probe is already running. The first answer
        // clears the flag and the queued one sets it again, so a single "not
        // checking" read catches the gap between them rather than the end. Quiet for
        // a moment is the answer.
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

        Assert.Fail($"no quiet period in 30 s; status is '{viewModel.CapabilityStatus}'");
    }

    [AvaloniaFact]
    public void AnEmptyCacheOffersNoModelAndSaysWhereToGetOne()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            WithEmptyCache();
            TranscriptionViewModel viewModel = Transcription();

            Assert.Empty(viewModel.AvailableModels);
            Assert.False(viewModel.HasDownloadedModels);
            Assert.False(viewModel.CanTranscribe);

            // Never empty, and it names the tab rather than a vague complaint.
            Assert.Contains("Models tab", viewModel.ModelPickerHint, StringComparison.Ordinal);
            Assert.Contains("Nothing on disk", viewModel.ModelPickerHint, StringComparison.Ordinal);
        }
        finally
        {
            Restore(previous, hadOverride);
        }
    }

    [AvaloniaFact]
    public void OnlyDownloadedModelsAreOffered()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            WithEmptyCache();
            ModelDescriptor first = ModelStore.Catalog[0];
            Plant(first);

            TranscriptionViewModel viewModel = Transcription();
            Settle(viewModel);

            Assert.Equal(new[] { first.Alias }, viewModel.AvailableModels);
            Assert.True(viewModel.HasDownloadedModels);
            Assert.True(viewModel.CanTranscribe,
                $"has={viewModel.HasDownloadedModels} checking={viewModel.IsCheckingCapabilities} " +
                $"transcribing={viewModel.IsTranscribing} alias='{viewModel.ModelAlias}'");

            // The selection has to follow the list: an alias not on disk would show
            // as nothing selected while still starting a run.
            Assert.Equal(first.Alias, viewModel.ModelAlias);

            // Counts, not a recommendation — this project ranks nothing by accuracy.
            Assert.Contains($"1 of {ModelStore.Catalog.Count} on disk", viewModel.ModelPickerHint, StringComparison.Ordinal);
        }
        finally
        {
            Restore(previous, hadOverride);
        }
    }

    [AvaloniaFact]
    public void ComingBackFromTheModelsTabShowsWhatWasDownloaded()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            WithEmptyCache();
            TranscriptionViewModel viewModel = Transcription();
            Assert.Empty(viewModel.AvailableModels);

            // The reader went to the Models tab and downloaded one. The pickers are
            // built when the window opens, so nothing updates on its own.
            ModelDescriptor downloaded = ModelStore.Catalog[1];
            Plant(downloaded);

            viewModel.RefreshModels();
            Settle(viewModel);

            Assert.Equal(new[] { downloaded.Alias }, viewModel.AvailableModels);
            Assert.True(viewModel.CanTranscribe);
            Assert.Equal(downloaded.Alias, viewModel.ModelAlias);
        }
        finally
        {
            Restore(previous, hadOverride);
        }
    }

    [AvaloniaFact]
    public void DeletingAModelTakesItOutOfThePicker()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            WithEmptyCache();
            ModelDescriptor first = ModelStore.Catalog[0];
            Plant(first);
            TranscriptionViewModel viewModel = Transcription();
            Settle(viewModel);
            Assert.True(viewModel.CanTranscribe);

            Assert.True(ModelStore.Delete(first));
            viewModel.RefreshModels();

            // Otherwise a run would start against a file that is no longer there.
            Assert.Empty(viewModel.AvailableModels);
            Assert.False(viewModel.CanTranscribe);
            Assert.Equal(string.Empty, viewModel.ModelAlias);
        }
        finally
        {
            Restore(previous, hadOverride);
        }
    }

    [AvaloniaFact]
    public void TheGetMoreButtonAsksForTheModelTab()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            WithEmptyCache();
            MainWindowViewModel window = MainWindow();

            Assert.NotEqual(MainWindowViewModel.ModelTabIndex, window.SelectedTabIndex);

            // Every tab that offers the button has to end up in the same place; the
            // event is what carries the request, since no view model can reach the
            // window itself.
            window.Transcription.OpenModelManagerCommand.Execute(null);
            Assert.Equal(MainWindowViewModel.ModelTabIndex, window.SelectedTabIndex);

            window.SelectedTabIndex = 0;
            window.Batch.OpenModelManagerCommand.Execute(null);
            Assert.Equal(MainWindowViewModel.ModelTabIndex, window.SelectedTabIndex);

            window.SelectedTabIndex = 0;
            window.Streaming.OpenModelManagerCommand.Execute(null);
            Assert.Equal(MainWindowViewModel.ModelTabIndex, window.SelectedTabIndex);
        }
        finally
        {
            Restore(previous, hadOverride);
        }
    }

    /// <summary>
    /// The tab index is stated in the view model, so it has to agree with what the
    /// window actually declares.
    /// </summary>
    [AvaloniaFact]
    public void TheModelIndexConstantPointsAtTheModelsTab()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            WithEmptyCache();
            // Read without showing: a MainWindow is a Window, and putting a Window
            // inside another one's Content is not something Avalonia allows. The tabs
            // are declared in XAML, so they are in the logical tree already.
            var mainWindow = new MainWindow { DataContext = MainWindow() };

            // The whole window, results tab included, holds three TabControls — the
            // transcription results grid has its own. Only the one bound to
            // SelectedTabIndex is being asked about.
            TabControl main = mainWindow.GetLogicalDescendants().OfType<TabControl>()
                .First(c => c.DataContext is MainWindowViewModel);
            List<TabItem> tabs = main.GetLogicalChildren().OfType<TabItem>().ToList();
            Assert.True(MainWindowViewModel.ModelTabIndex < tabs.Count,
                $"the constant says index {MainWindowViewModel.ModelTabIndex}, the window has {tabs.Count} tabs");
            Assert.Equal("Models", tabs[MainWindowViewModel.ModelTabIndex].Header?.ToString());
        }
        finally
        {
            Restore(previous, hadOverride);
        }
    }

    private static MainWindowViewModel MainWindow()
        => new(
            Transcription(),
            new StreamingViewModel(new StubTranscriptionService(), new SettingsViewModel()),
            new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel()),
            new ModelManagerViewModel(),
            new SettingsViewModel());

    private static void Restore(string previous, bool hadOverride)
        => ModelStore.CacheRootOverride = hadOverride ? previous : null;
}