using Tweakora.App.ViewModels;
using Wpf.Ui.Controls;

namespace Tweakora.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
