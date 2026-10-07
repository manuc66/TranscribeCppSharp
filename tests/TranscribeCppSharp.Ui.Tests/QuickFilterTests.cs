using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The quick filters actually reach the view model, rather than rendering as a box
/// that does nothing.
/// </summary>
/// <remarks>
/// The size picker was bound to a <c>SelectedItem</c> holding a
/// <see cref="ModelSizeFilter"/> enum while its items are
/// <see cref="ModelSizeFilterOption"/> records. The two never match, so the box
/// showed nothing selected and a click could not reach the filter — a column of
/// the interface that looked present and was inert. These tests assert the round
/// trip from the control to the rows, which is the only thing that notices.
/// </remarks>
public class QuickFilterTests
{
    private static (Window Window, ModelManagerViewModel ViewModel, MainWindowViewModel Root) Show()
    {
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-ui-tests-quick-filters");

        var root = new MainWindowViewModel(
            new TranscriptionViewModel(new StubTranscriptionService(), new SettingsViewModel()),
            new StreamingViewModel(new StubTranscriptionService(), new SettingsViewModel()),
            new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel()),
            new ModelManagerViewModel(),
            new SettingsViewModel());

        var window = new MainWindow { DataContext = root };
        window.Show();
        root.SelectedTabIndex = MainWindowViewModel.ModelTabIndex;
        Settle(window);

        return (window, root.ModelManager, root);
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>Small enough to be a subset of the catalogue, in bytes.</summary>
    private const long Under500Mb = 500L * 1024 * 1024;

    [AvaloniaFact]
    public void PickingASizeNarrowsTheGrid()
    {
        var (window, viewModel, _) = Show();
        ComboBox size = Find(window, "models-size-filter");

        // The picker shows the option for the band, not a blank box.
        Assert.NotEmpty(size.Items);
        Assert.Equal(ModelSizeFilter.Any, size.SelectedValue);

        // Choose the band, as a click would.
        ModelSizeFilterOption small = viewModel.SizeFilters
            .Single(o => o.Value == ModelSizeFilter.Small);
        size.SelectedItem = small;
        Settle(window);

        // The control wrote through to the view model, and the rows followed.
        Assert.Equal(ModelSizeFilter.Small, viewModel.SizeFilter);
        Assert.NotEmpty(viewModel.VisibleModels);
        Assert.All(viewModel.VisibleModels,
            m => Assert.True(m.Descriptor.Size < Under500Mb,
                $"{m.Alias} is {m.Descriptor.Size} bytes, over the band's 500 MB"));
    }

    [AvaloniaFact]
    public void TheSizeFilterCountsAsSomethingToClear()
    {
        var (window, viewModel, _) = Show();
        ComboBox size = Find(window, "models-size-filter");

        size.SelectedItem = viewModel.SizeFilters.Single(o => o.Value == ModelSizeFilter.Large);
        Settle(window);

        Assert.True(viewModel.HasAnyQuickFilter,
            "Clear is bound to this, so a size band must count as something to clear");

        // The band's own label, not a string the test invented: the summary quotes
        // it, so the two are the same text by construction.
        string label = viewModel.SizeFilters
            .Single(o => o.Value == ModelSizeFilter.Large).Label.ToLowerInvariant();
        Assert.Contains(label, viewModel.ActiveFilterSummary.ToLowerInvariant(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every choice offered is one something can be found for.
    /// </summary>
    /// <remarks>
    /// Built from the records, so this is checking the construction rather than
    /// discovering a fact: a key written into the picker by hand would be the kind of
    /// choice that returns an empty grid and reads as a broken catalogue.
    /// </remarks>
    [Fact]
    public void EveryCapabilityOfferedIsDeclaredBySomeModel()
    {
        ModelManagerViewModel viewModel = new();

        Assert.NotEmpty(viewModel.Features);
        Assert.Equal(viewModel.Features.Distinct(StringComparer.Ordinal), viewModel.Features);

        foreach (string key in viewModel.Features)
        {
            Assert.Contains(ModelStore.Catalog, d => new ModelCatalogItem(d).Supports(key));
        }
    }

    [Fact]
    public void AModelWithNoRecordDoesNotHaveTheFeature()
    {
        // Unknown is not yes: the filter must not keep a model whose capabilities
        // nobody has recorded, or a filtered grid would show rows nobody can check.
        var item = new ModelCatalogItem(new ModelDescriptor(
            Alias: "invented-alias",
            Repo: "someone/invented-gguf",
            Revision: "rev",
            License: "mit",
            LicenseUrl: "https://example.invalid",
            Quant: "Q8_0",
            File: "invented.gguf",
            Sha256: "00",
            Size: 1));

        Assert.False(item.Supports("streaming"));
        Assert.False(item.Supports("diarize"));
    }

    [AvaloniaFact]
    public void PickingACapabilityNarrowsTheGrid()
    {
        var (window, viewModel, _) = Show();
        ComboBox features = Find(window, "models-feature-filter");

        Assert.NotEmpty(features.Items);
        Assert.Null(features.SelectedItem);

        features.SelectedItem = "streaming";
        Settle(window);

        Assert.Equal("streaming", viewModel.FeatureFilter);

        Assert.NotEmpty(viewModel.VisibleModels);
        Assert.True(viewModel.VisibleModels.Count < ModelStore.Catalog.Count,
            "the filter hid nothing, which is not filtering");
        Assert.All(viewModel.VisibleModels, m => Assert.True(m.Supports("streaming")));

        // And a model that does not stream is gone — the negative is what proves
        // the filter excludes rather than merely being set.
        ModelCatalogItem whisper = ModelStore.Catalog
            .Where(d => d.Alias == "whisper-tiny")
            .Select(d => new ModelCatalogItem(d))
            .Single();
        Assert.False(whisper.Supports("streaming"));
        Assert.DoesNotContain(viewModel.VisibleModels, m => m.Alias == "whisper-tiny");

        Assert.Contains("capability streaming", viewModel.ActiveFilterSummary,
            StringComparison.Ordinal);
    }

    /// <summary>Clear covers every filter, including the last one added.</summary>
    [AvaloniaFact]
    public void ClearDropsTheCapabilityAlongWithTheRest()
    {
        var (window, viewModel, _) = Show();
        ComboBox features = Find(window, "models-feature-filter");

        features.SelectedItem = "streaming";
        Settle(window);
        Assert.Equal("streaming", viewModel.FeatureFilter);

        // Clear is a list of assignments: one left out and the button says the list
        // is clean while a row is still hidden.
        viewModel.ClearFiltersCommand.Execute(null);

        Assert.Null(viewModel.FeatureFilter);
        Assert.False(viewModel.HasAnyQuickFilter);
        Assert.Equal(ModelStore.Catalog.Count, viewModel.VisibleModels.Count);
    }

    /// <summary>Locate a control by the automation id the tests pin.</summary>
    private static ComboBox Find(Window window, string id)
    {
        ComboBox? combo = window.GetVisualDescendants()
            .OfType<ComboBox>()
            .FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id);
        return combo ?? throw new InvalidOperationException($"no ComboBox with id '{id}'");
    }
}