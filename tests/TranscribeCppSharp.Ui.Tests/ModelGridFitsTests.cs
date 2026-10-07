using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// The model grid has to fit in the window it is opened in.
/// </summary>
/// <remarks>
/// Star-sized columns are sized proportionally, but only down to the width their
/// headers and content demand: once those minima exceed the viewport the extra
/// columns are simply cut off, and the last one — Action — is the one you cannot
/// see. A screenshot would show it, but a test states it in pixels, so this is a
/// measurement rather than an impression.
/// </remarks>
public class ModelGridFitsTests
{
    [AvaloniaFact]
    public void EveryActionButtonIsInsideTheGrid()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            ModelStore.CacheRootOverride = Path.Combine(
                Path.GetTempPath(), "tcsharp-ui-tests-grid-fits");

            var viewModel = new ModelManagerViewModel { DownloadedOnly = false };
            var view = new ModelManagerView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show();
            DispatcherPump(window);

            DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().First();
            Assert.True(grid.Bounds.Width > 0, "the grid did not lay out");

            // Realized rows only: the grid virtualizes, so asking about rows that
            // were never rendered would compare nothing against nothing.
            List<Button> buttons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(b => b.IsVisible && b.Content is string c && c == "Delete")
                .ToList();
            Assert.NotEmpty(buttons);

            // Either it fits, or the grid can be scrolled to it. What is not
            // acceptable is content beyond the viewport with no way to reach it:
            // that is not "you have to scroll", it is a button that does not exist.
            bool scrollable = grid.HorizontalScrollBarVisibility is
                ScrollBarVisibility.Auto or ScrollBarVisibility.Visible;

            foreach (Button button in buttons)
            {
                Point? right = button.TranslatePoint(
                    new Point(button.Bounds.Width, 0), grid);
                Assert.NotNull(right);

                if (right.Value.X > grid.Bounds.Width)
                {
                    // A property set to Auto proves nothing on its own: the template
                    // decides whether one is actually built. Overflow plus no visible
                    // bar is the case that loses a button.
                    string bars = string.Join(" | ", grid.GetVisualDescendants()
                        .OfType<ScrollBar>()
                        .Select(b => $"{b.Orientation} vis={b.IsVisible} w={b.Bounds.Width}"));
                    bool hasBar = grid.GetVisualDescendants()
                        .OfType<ScrollBar>()
                        .Any(b => b.IsVisible && b.Orientation.ToString() == "Horizontal");

                    Assert.True(hasBar,
                        $"Delete reaches x={right.Value.X:F0} but the grid is only " +
                        $"{grid.Bounds.Width:F0} wide at a {window.Width:F0}px window, " +
                        $"Delete reaches x={right.Value.X:F0} but the grid is only " +
                        $"{grid.Bounds.Width:F0} wide at a {window.Width:F0}px window, " +
                        "and no horizontal scrollbar is rendered — unreachable. " +
                        $"Bars: [{bars}]");
                }
            }
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    /// <summary>Layout has to run before anything can be measured.</summary>
    private static void DispatcherPump(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>
    /// Nothing the reader can press or open may sit outside the window.
    /// </summary>
    /// <remarks>
    /// The filter row above the grid takes a control each time a filter is added, and
    /// the row is measured in autos, so one more is not obviously free: it pushes the
    /// Clear button out the same way Action was pushed out. This is the guard for
    /// adding controls to that row at all.
    /// </remarks>
    [AvaloniaFact]
    public void EveryControlInTheModelsViewIsReachable()
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        try
        {
            ModelStore.CacheRootOverride = Path.Combine(
                Path.GetTempPath(), "tcsharp-ui-tests-view-fits");

            var view = new ModelManagerView { DataContext = new ModelManagerViewModel() };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show();
            DispatcherPump(window);

            List<Control> controls = window.GetVisualDescendants()
                .OfType<Control>()
                .Where(c => c.IsVisible
                    && c.DataContext is ModelManagerViewModel
                    && (c is Button || c is ComboBox || c is TextBox || c is CheckBox))
                .ToList();
            Assert.NotEmpty(controls);

            var outside = new List<string>();
            foreach (Control control in controls)
            {
                Point? right = control.TranslatePoint(
                    new Point(control.Bounds.Width, 0), window);
                if (right is null || right.Value.X > window.Bounds.Width)
                {
                    string? id = AutomationProperties.GetAutomationId(control);
                    string what = !string.IsNullOrEmpty(id) ? id
                        : control is Button b ? $"button {b.Content}"
                        : control.GetType().Name;
                    outside.Add($"{what} at x={right?.X:F0}");
                }
            }

            Assert.True(outside.Count == 0,
                $"{outside.Count} control(s) beyond the {window.Bounds.Width:F0}px window: " +
                string.Join(", ", outside));

            // Reachable but collapsed would be the other way to lose a control: a
            // WrapPanel measures against infinite width, so the search box can end up
            // sized to its padding alone.
            Control search = controls.First(c =>
                AutomationProperties.GetAutomationId(c) == "models-filter");
            Assert.True(search.Bounds.Width >= 250,
                $"the search box is {search.Bounds.Width:F0}px wide, too narrow to type into");
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }
}