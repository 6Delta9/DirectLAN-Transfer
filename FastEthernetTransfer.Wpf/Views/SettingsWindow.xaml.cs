using System.Windows;
using FastEthernetTransfer.Wpf.ViewModels;

namespace FastEthernetTransfer.Wpf.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
    }
}
