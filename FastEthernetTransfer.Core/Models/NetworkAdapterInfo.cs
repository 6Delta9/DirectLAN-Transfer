using System.Net;
using System.Net.NetworkInformation;

namespace FastEthernetTransfer.Core.Models;

public class NetworkAdapterInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public NetworkInterfaceType InterfaceType { get; set; }
    public OperationalStatus Status { get; set; }
    public long SpeedBitsPerSec { get; set; }
    public IPAddress? IpAddress { get; set; }
    public IPAddress? SubnetMask { get; set; }
    public IPAddress? BroadcastAddress { get; set; }
    public bool IsPhysicalEthernet { get; set; }
    public bool IsWireless { get; set; }
    public bool IsVirtualOrVpn { get; set; }

    public string SpeedFormatted
    {
        get
        {
            if (SpeedBitsPerSec >= 10_000_000_000L) return "10 Gbps";
            if (SpeedBitsPerSec >= 5_000_000_000L) return "5 Gbps";
            if (SpeedBitsPerSec >= 2_500_000_000L) return "2.5 Gbps";
            if (SpeedBitsPerSec >= 1_000_000_000L) return "1 Gbps";
            if (SpeedBitsPerSec >= 100_000_000L) return "100 Mbps";
            if (SpeedBitsPerSec > 0) return $"{SpeedBitsPerSec / 1_000_000.0:F0} Mbps";
            return "Active";
        }
    }

    public string DisplaySummary => $"{Name} ({Description}) - {SpeedFormatted} [{IpAddress}]";
}
