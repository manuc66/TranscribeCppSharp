using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TranscribeCppSharp.Ui.Views;

public partial class ModelManagerView : UserControl
{
    public ModelManagerView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
