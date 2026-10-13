using System;

namespace FastEthernetTransfer.Core.Models;

public class PeerInfo
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 45779;
    public string InterfaceType { get; set; } = "Ethernet";
    public long LinkSpeedBitsPerSec { get; set; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public bool IsTrusted { get; set; }
    public int ProtocolVersion { get; set; } = 1;

    public string LinkSpeedFormatted
    {
        get
        {
            if (LinkSpeedBitsPerSec >= 10_000_000_000L) return "10 Gbps";
            if (LinkSpeedBitsPerSec >= 5_000_000_000L) return "5 Gbps";
            if (LinkSpeedBitsPerSec >= 2_500_000_000L) return "2.5 Gbps";
            if (LinkSpeedBitsPerSec >= 1_000_000_000L) return "1 Gbps";
            if (LinkSpeedBitsPerSec >= 100_000_000L) return "100 Mbps";
            if (LinkSpeedBitsPerSec > 0) return $"{LinkSpeedBitsPerSec / 1_000_000.0:F0} Mbps";
            return "Unknown";
        }
    }

    public override string ToString() => $"{DeviceName} ({IpAddress}:{Port} - {LinkSpeedFormatted})";
}
