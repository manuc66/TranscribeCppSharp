using Avalonia.Controls;
using Avalonia.Markup.Xaml;

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
}
