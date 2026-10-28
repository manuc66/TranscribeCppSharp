using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.ViewModels;

namespace TranscribeCppSharp.UI.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Mirrors the grid's selection into the view model, which keeps the bulk
    /// delete logic testable without a control tree.
    /// </summary>
    /// <remarks>
    /// The DataGrid's SelectedItems is an IList that the control owns; binding it
    /// to the view model's collection is not accepted by the compiled XAML, so the
    /// view copies it across on each change instead.
    /// </remarks>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is HistoryViewModel model && sender is DataGrid grid)
        {
            model.UpdateSelection(grid.SelectedItems.OfType<HistoryEntry>());
        }
    }

    /// <summary>
    /// Shows the history folder in the file manager.
    /// </summary>
    /// <remarks>
    /// Kept in the view: the launcher belongs to the window, and the view model
    /// has no business talking to the OS. A failure is reported in the tab's
    /// status line rather than thrown at a click handler.
    /// </remarks>
    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel model)
        {
            return;
        }

        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top?.Launcher is not { } launcher || top.StorageProvider is null)
        {
            model.StatusMessage = "This window cannot open a folder.";
            return;
        }

        try
        {
            // Avalonia's launcher only takes a storage item, so the folder is
            // resolved through the storage provider first. A folder that does not
            // exist yet resolves to null, which is the normal state before the
            // first saved run.
            IStorageFolder? folder = await top.StorageProvider.TryGetFolderFromPathAsync(new Uri(model.HistoryPath));
            if (folder is null)
            {
                model.StatusMessage = $"{model.HistoryPath} does not exist yet.";
                return;
            }

            if (!await launcher.LaunchFileAsync(folder))
            {
                model.StatusMessage = $"No file manager opened {model.HistoryPath}.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            model.StatusMessage = $"Could not open the history folder: {ex.Message}";
        }
    }
}
