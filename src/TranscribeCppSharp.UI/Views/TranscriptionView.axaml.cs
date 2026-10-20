using Avalonia.Controls;
using Avalonia.Input;
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
