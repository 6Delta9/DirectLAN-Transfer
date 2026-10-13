using System.Windows;
using System.Windows.Controls;
using FastEthernetTransfer.Wpf.ViewModels;

namespace FastEthernetTransfer.Wpf.Views;

public partial class GameUpdaterView : UserControl
{
    public GameUpdaterView()
    {
        InitializeComponent();
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
}
