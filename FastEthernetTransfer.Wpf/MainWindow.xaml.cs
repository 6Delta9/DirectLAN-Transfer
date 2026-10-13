using System.Windows;
using FastEthernetTransfer.Wpf.ViewModels;
using FastEthernetTransfer.Wpf.Views;

namespace FastEthernetTransfer.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (DataContext is MainViewModel vm)
            {
                vm.IsDragOver = true;
            }
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnDropZoneDragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsDragOver = false;
        }
    }

    private void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsDragOver = false;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped != null && dropped.Length > 0)
                {
                    vm.HandleDroppedItems(dropped);
                }
                e.Handled = true;
            }
        }
    }

    private void OnOpenGameUpdaterClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.CurrentNavSection = NavSection.GameUpdater;
        }
    }

    private void OnOpenBenchmarkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var benchVm = new BenchmarkViewModel(vm.NicDetector);
            var benchWindow = new BenchmarkWindow(benchVm)
            {
                Owner = this
            };
            benchWindow.ShowDialog();
        }
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var settingsVm = new SettingsViewModel(vm.SettingsService);
            var settingsWindow = new SettingsWindow(settingsVm)
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
            vm.RefreshNetworkInfo();
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is MainViewModel vm)
        {
            vm.Dispose();
        }
    }
}