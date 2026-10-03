using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TranscribeCppSharp.Ui.Views;

public partial class BatchView : UserControl
{
    public BatchView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
