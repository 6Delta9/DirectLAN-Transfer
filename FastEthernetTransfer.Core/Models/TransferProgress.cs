using System;

namespace FastEthernetTransfer.Core.Models;

public class TransferProgress
{
    public Guid TransferId { get; set; }
    public TransferDirection Direction { get; set; }
    public TransferState State { get; set; } = TransferState.Idle;
    public string PeerName { get; set; } = string.Empty;
    public string CurrentFileName { get; set; } = string.Empty;
    public int CurrentFileIndex { get; set; }
    public int TotalFiles { get; set; }
    public int CompletedFiles { get; set; }

    public long CurrentFileBytesTransferred { get; set; }
    public long CurrentFileSize { get; set; }

    public long TotalBytesTransferred { get; set; }
    public long TotalBytes { get; set; }

    public double CurrentSpeedBytesPerSec { get; set; }
    public double AverageSpeedBytesPerSec { get; set; }
    public double PeakSpeedBytesPerSec { get; set; }

    public TimeSpan EstimatedTimeRemaining { get; set; }
    public string? ErrorMessage { get; set; }

    public double PercentComplete => TotalBytes > 0 ? Math.Min(100.0, (double)TotalBytesTransferred / TotalBytes * 100.0) : 0.0;
    public double CurrentFilePercentComplete => CurrentFileSize > 0 ? Math.Min(100.0, (double)CurrentFileBytesTransferred / CurrentFileSize * 100.0) : 0.0;

    public long RemainingBytes => Math.Max(0L, TotalBytes - TotalBytesTransferred);

    // Formatted properties for clean UI binding
    public string CurrentSpeedFormatted => FormatSpeed(CurrentSpeedBytesPerSec);
    public string NetworkSpeedGbpsFormatted => FormatGbps(CurrentSpeedBytesPerSec);
    public string AverageSpeedFormatted => FormatSpeed(AverageSpeedBytesPerSec);
    public string PeakSpeedFormatted => FormatSpeed(PeakSpeedBytesPerSec);

    public string TotalTransferredFormatted => FormatBytes(TotalBytesTransferred);
    public string TotalBytesFormatted => FormatBytes(TotalBytes);
    public string RemainingBytesFormatted => FormatBytes(RemainingBytes);

    public string EtaFormatted
    {
        get
        {
            if (State == TransferState.Completed) return "Completed";
            if (State == TransferState.Paused) return "Paused";
            if (CurrentSpeedBytesPerSec <= 0 || TotalBytesTransferred >= TotalBytes) return "--";

            if (EstimatedTimeRemaining.TotalHours >= 1)
                return $"{(int)EstimatedTimeRemaining.TotalHours}h {EstimatedTimeRemaining.Minutes}m {EstimatedTimeRemaining.Seconds}s";
            if (EstimatedTimeRemaining.TotalMinutes >= 1)
                return $"{EstimatedTimeRemaining.Minutes}m {EstimatedTimeRemaining.Seconds}s";
            return $"{Math.Max(1, EstimatedTimeRemaining.Seconds)}s";
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0 * 1024.0):F2} TB";
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1024.0 * 1024.0 * 1024.0) return $"{bytesPerSec / (1024.0 * 1024.0 * 1024.0):F2} GB/s";
        if (bytesPerSec >= 1024.0 * 1024.0) return $"{bytesPerSec / (1024.0 * 1024.0):F1} MB/s";
        if (bytesPerSec >= 1024.0) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec:F0} B/s";
    }

    public static string FormatGbps(double bytesPerSec)
    {
        double bitsPerSec = bytesPerSec * 8.0;
        if (bitsPerSec >= 1_000_000_000.0) return $"{bitsPerSec / 1_000_000_000.0:F2} Gbps";
        if (bitsPerSec >= 1_000_000.0) return $"{bitsPerSec / 1_000_000.0:F1} Mbps";
        return $"{bitsPerSec / 1_000.0:F0} Kbps";
    }
}
