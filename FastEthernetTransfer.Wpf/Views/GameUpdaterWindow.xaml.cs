using System;
using System.Collections.ObjectModel;
using System.Windows;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Network;
using FastEthernetTransfer.Core.Storage;
using FastEthernetTransfer.Wpf.ViewModels;

namespace FastEthernetTransfer.Wpf.Views;

public partial class GameUpdaterWindow : Window
{
    public GameUpdaterWindow(ObservableCollection<PeerInfo> peers, SettingsService settings, NetworkInterfaceDetector nicDetector)
    {
        InitializeComponent();
        var vm = new GameUpdaterViewModel(peers, settings, nicDetector);
        vm.RequestClose += Close;
        DataContext = vm;
    }

    private void OnDestinationFolderDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnDestinationFolderDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0 && DataContext is GameUpdaterViewModel vm)
            {
                vm.HandleDroppedFolder(files[0]);
            }
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
