using System;

namespace FastEthernetTransfer.Core.Models;

public class TransferItem
{
    public int ItemIndex { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
    public DateTime LastWriteTimeUtc { get; set; } = DateTime.UtcNow;
    public string? ExpectedSha256 { get; set; }

    public string SizeFormatted
    {
        get
        {
            if (IsDirectory) return "Folder";
            if (Size >= 1024L * 1024L * 1024L * 1024L) return $"{Size / (1024.0 * 1024.0 * 1024.0 * 1024.0):F2} TB";
            if (Size >= 1024L * 1024L * 1024L) return $"{Size / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (Size >= 1024L * 1024L) return $"{Size / (1024.0 * 1024.0):F2} MB";
            if (Size >= 1024L) return $"{Size / 1024.0:F2} KB";
            return $"{Size} B";
        }
    }
}
