using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Copy button beside Export writes the whole transcript to the clipboard.
/// The text is also selectable, so this is the whole-result shortcut rather than
/// the only way to copy anything.
/// </summary>
[Collection("UiGlobalState")]
public class TranscriptionCopyTests
{
    [AvaloniaFact]
    public async Task CopyPutsTheWholeTranscriptOnTheClipboard()
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-copy-" + Guid.NewGuid().ToString("N"));
        using var settings = new UiSettingsScope();
        try
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache())
            {
                FullText = "first line\nsecond line",
                HasResult = true,
            };
            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();

            var copy = view.GetLogicalDescendants()
                .OfType<Button>()
                .First(b => AutomationProperties.GetAutomationId(b) == "transcription-copy");

            Assert.True(copy.IsEnabled);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, copy));

            // The handler is async void: give its continuation a turn before
            // reading the clipboard and the status line it sets.
            for (int i = 0; i < 10 && viewModel.StatusMessage.Length == 0; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            Assert.NotNull(window.Clipboard);
            Assert.Equal("first line\nsecond line", await window.Clipboard.TryGetTextAsync());
            Assert.Contains("clipboard", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }
}
