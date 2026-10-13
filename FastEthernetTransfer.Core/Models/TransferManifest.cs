using System;
using System.Collections.Generic;

namespace FastEthernetTransfer.Core.Models;

public class TransferManifest
{
    public Guid TransferId { get; set; } = Guid.NewGuid();
    public Guid SenderId { get; set; }
    public string SenderDeviceName { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
    public int TotalFolders { get; set; }
    public List<TransferItem> Items { get; set; } = [];

    public string TotalSizeFormatted
    {
        get
        {
            if (TotalBytes >= 1024L * 1024L * 1024L * 1024L) return $"{TotalBytes / (1024.0 * 1024.0 * 1024.0 * 1024.0):F2} TB";
            if (TotalBytes >= 1024L * 1024L * 1024L) return $"{TotalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (TotalBytes >= 1024L * 1024L) return $"{TotalBytes / (1024.0 * 1024.0):F2} MB";
            if (TotalBytes >= 1024L) return $"{TotalBytes / 1024.0:F2} KB";
            return $"{TotalBytes} B";
        }
    }
}
