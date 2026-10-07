using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.ViewModels;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The Models grid can be narrowed by the language a model declares.
/// </summary>
/// <remarks>
/// Filtering by language is why a reader opens this tab: licence and size answer
/// narrow questions, language answers the one most people arrive with. Every
/// assertion here is about the model card's claim, which is all the manifest has —
/// it is not a statement about accuracy.
/// </remarks>
public class ModelLanguageFilterTests
{
    /// <summary>A language plenty of models do not declare, so the filter has work to do.</summary>
    private const string CommonLanguage = "fr";

    private static ModelManagerViewModel Filtered(string? language)
        => new() { LanguageFilter = language };

    [Fact]
    public void SelectingALanguageNarrowsTheCatalogue()
    {
        ModelManagerViewModel models = Filtered(CommonLanguage);

        Assert.True(models.VisibleModels.Count > 0, "nothing matched a language most models declare");
        Assert.True(models.VisibleModels.Count < ModelStore.Catalog.Count,
            "every model declares the same language, so the filter proved nothing");

        Assert.All(models.VisibleModels,
            m => Assert.Contains(CommonLanguage, m.Descriptor.Languages ?? Array.Empty<string>()));
    }

    /// <summary>
    /// A picker that offers a language nothing declares is a filter that returns an
    /// empty grid and no explanation.
    /// </summary>
    [Fact]
    public void EveryOfferedLanguageIsDeclaredBySomeModel()
    {
        ModelManagerViewModel models = Filtered(null);

        Assert.NotEmpty(models.Languages);
        foreach (string code in models.Languages)
        {
            Assert.Contains(ModelStore.Catalog,
                d => d.Languages?.Contains(code, StringComparer.Ordinal) == true);
        }
    }

    /// <summary>
    /// The free-text box has to reach the codes too, or the search and the picker
    /// answer the same question differently.
    /// </summary>
    [Fact]
    public void FreeTextReachesLanguages()
    {
        ModelManagerViewModel byText = new() { Filter = CommonLanguage };
        ModelManagerViewModel byPicker = Filtered(CommonLanguage);

        Assert.True(byText.VisibleModels.Count > 0);

        // By alias, not by instance: the two view models each build their own rows,
        // so reference comparison would fail for reasons that have nothing to do with
        // what the search found.
        HashSet<string> found = byText.VisibleModels.Select(m => m.Alias).ToHashSet();
        foreach (ModelCatalogItem shown in byPicker.VisibleModels)
        {
            Assert.Contains(shown.Alias, found);
        }
    }

    [Fact]
    public void ClearResetsTheLanguageFilter()
    {
        ModelManagerViewModel models = new()
        {
            LanguageFilter = CommonLanguage,
            DownloadedOnly = true,
        };
        Assert.NotEqual(ModelStore.Catalog.Count, models.VisibleModels.Count);

        models.ClearFiltersCommand.Execute(null);

        Assert.Null(models.LanguageFilter);
        Assert.Equal(ModelStore.Catalog.Count, models.VisibleModels.Count);
    }

    /// <summary>
    /// A filter that hides rows silently is how a reader concludes the model they
    /// want is missing from the catalogue.
    /// </summary>
    [Fact]
    public void TheSummaryNamesTheLanguageInForce()
    {
        ModelManagerViewModel models = Filtered(CommonLanguage);

        Assert.Contains($"language {CommonLanguage}", models.ActiveFilterSummary, StringComparison.Ordinal);
        Assert.True(models.HasAnyQuickFilter,
            "Clear is bound to this, so a language-only filter must count as something to clear");
    }
}