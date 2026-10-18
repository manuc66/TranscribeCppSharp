using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The read-only text panels are selectable, so a reader can copy from them.
/// </summary>
/// <remarks>
/// A plain <see cref="TextBlock"/> draws text but takes no selection. The panels
/// are <see cref="SelectableTextBlock"/>, which Fluent gives mouse selection,
/// Ctrl+C / Ctrl+A and a right-click Copy flyout. These tests pin the panel type,
/// because a screenshot cannot show that a selection is possible and the
/// automation ids would still pass if the control went back to a TextBlock.
/// </remarks>
public class SelectableTextTests
{
    [AvaloniaFact]
    public void StreamingTextAreasAreSelectable()
    {
        var view = new StreamingView();

        AssertSelectable(view, "streaming-committed-text");
        AssertSelectable(view, "streaming-tentative-text");
    }

    [AvaloniaFact]
    public void TranscriptionTextAreaIsSelectable()
    {
        var view = new TranscriptionView();

        Assert.Contains(view.GetLogicalDescendants().OfType<Control>(),
            control => control is SelectableTextBlock);
    }

    private static void AssertSelectable(Control view, string automationId)
    {
        Control? control = view.GetLogicalDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == automationId);

        Assert.NotNull(control);
        Assert.IsType<SelectableTextBlock>(control);
    }
}
