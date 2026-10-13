namespace FastEthernetTransfer.Wpf.ViewModels;

public class SelectedItemViewModel
{
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;
    public string ItemType => IsDirectory ? "Folder" : "File";
    public string IconText => IsDirectory ? "📁" : "📄";
}
