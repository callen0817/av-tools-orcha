using System.Windows;
using AV.Orch.Desktop.ViewModels;

namespace AV.Orch.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
