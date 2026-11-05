using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BALAN.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
