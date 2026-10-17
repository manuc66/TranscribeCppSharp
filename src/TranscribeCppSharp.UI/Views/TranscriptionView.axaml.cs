using Avalonia.Controls;
using Avalonia.Markup.Xaml;

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
}
