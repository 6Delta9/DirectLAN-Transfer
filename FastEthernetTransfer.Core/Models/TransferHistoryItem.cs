using System;

namespace FastEthernetTransfer.Core.Models;

public class TransferHistoryItem
{
    public Guid TransferId { get; set; } = Guid.NewGuid();
    public TransferDirection Direction { get; set; }
    public string PeerName { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
    public TransferState State { get; set; }
    public double AverageSpeedBytesPerSec { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }

    public string TotalBytesFormatted => TransferProgress.FormatBytes(TotalBytes);
    public string AverageSpeedFormatted => TransferProgress.FormatSpeed(AverageSpeedBytesPerSec);
    public string TimestampLocalFormatted => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string DirectionDisplay => Direction == TransferDirection.Send ? "Sent" : "Received";
    public string StateDisplay => State switch
    {
        TransferState.Completed => "Completed",
        TransferState.Cancelled => "Cancelled",
        TransferState.Failed => "Failed",
        TransferState.Transferring => "In Progress",
        _ => State.ToString()
    };
}
