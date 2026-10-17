using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TranscribeCppSharp.UI.Views;

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
