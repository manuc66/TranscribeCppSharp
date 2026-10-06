using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The model grid marks the alias the CLI defaults to, because the banner
/// above the grid sends the reader to the model marked 'default'.
/// </summary>
/// <remarks>
/// The marking used to be a property on <see cref="ModelCatalogItem"/> that
/// nothing was bound to, so the banner pointed at a mark no one could see: a
/// property without a binding fails silently, which is exactly why these tests
/// realize the rows and read the mark back instead of asserting on the model.
/// </remarks>
public class DefaultModelMarkingTests
{
    [AvaloniaFact]
    public void TheDefaultAliasRowIsMarked()
    {
        var (window, models) = ShowGridFilteredTo(ModelCatalogItem.DefaultAlias);

        // The precondition, asserted so a filter that started matching more
        // than one row fails here rather than quietly weakening the test below.
        Assert.Single(models.VisibleModels);
        Assert.True(models.VisibleModels[0].IsDefaultModel);

        var marks = DefaultMarks(window);
        Assert.NotEmpty(marks);
        Assert.Equal(1, marks.Count(m => m.IsVisible));
    }

    [AvaloniaFact]
    public void AnyOtherRowIsNotMarked()
    {
        var (window, models) = ShowGridFilteredTo("whisper-tiny.en");

        Assert.Single(models.VisibleModels);
        Assert.False(models.VisibleModels[0].IsDefaultModel);

        // The mark may exist in the tree, hidden. What must not happen is one
        // of them being shown on a row that is not the default.
        Assert.DoesNotContain(DefaultMarks(window), m => m.IsVisible);
    }

    /// <summary>Shows the grid with its filter narrowed to a single alias.</summary>
    private static (Window Window, ModelManagerViewModel Models) ShowGridFilteredTo(string alias)
    {
        var models = new ModelManagerViewModel { Filter = alias };
        var view = new ModelManagerView { DataContext = models };
        var window = new Window { Content = view, Width = 1100, Height = 720 };
        window.Show();
        return (window, models);
    }

    /// <summary>Every "default" mark in the realized tree, shown or hidden.</summary>
    private static List<TextBlock> DefaultMarks(Window window)
        => window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Text == "default")
            .ToList();
}