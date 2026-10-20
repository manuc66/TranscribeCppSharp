using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using TranscribeCppSharp.UI.ViewModels;

namespace TranscribeCppSharp.UI.Views;

public partial class TranscriptionView : UserControl
{
    public TranscriptionView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Copies the whole transcript, the counterpart to selecting a passage by
    /// hand. The clipboard comes from the top level rather than a cached field,
    /// so the handler works wherever the control is hosted.
    /// </summary>
    private async void OnCopyTranscript(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TranscriptionViewModel viewModel)
        {
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            viewModel.StatusMessage = "This window has no clipboard to copy to.";
            return;
        }

        try
        {
            await clipboard.SetTextAsync(viewModel.FullText);
            viewModel.StatusMessage = "Copied the transcript to the clipboard.";
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"Could not copy the transcript: {ex.Message}";
        }
    }

    /// <summary>
    /// Shows a copy cursor only over a file the tab would actually take. The
    /// filter is the view model's, not a second list here, so a drop and the
    /// Browse dialog accept the same files.
    /// </summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        IStorageItem[] files = e.DataTransfer.TryGetFiles() ?? [];
        bool accepted = files.Any(file =>
            TranscriptionViewModel.CanAcceptDroppedFile(file.TryGetLocalPath()));

        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>
    /// Takes the first dropped file the tab can run. A drop with nothing usable
    /// leaves the event unhandled rather than swallowing it.
    /// </summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not TranscriptionViewModel viewModel)
        {
            return;
        }

        foreach (IStorageItem file in e.DataTransfer.TryGetFiles() ?? [])
        {
            if (viewModel.TryAcceptDroppedFile(file.TryGetLocalPath()))
            {
                e.Handled = true;
                return;
            }
        }
    }
}
