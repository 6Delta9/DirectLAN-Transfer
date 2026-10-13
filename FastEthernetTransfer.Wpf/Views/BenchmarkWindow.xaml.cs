using System.Windows;
using FastEthernetTransfer.Wpf.ViewModels;

namespace FastEthernetTransfer.Wpf.Views;

public partial class BenchmarkWindow : Window
{
    public BenchmarkWindow(BenchmarkViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
