using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TranscribeCppSharp.Ui.Views;

public partial class StreamingView : UserControl
{
    public StreamingView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
