using System;
using System.Text.Json;

namespace FastEthernetTransfer.Core.Network;

public class DiscoveryBeacon
{
    public int ProtocolVersion { get; set; } = 1;
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int TcpPort { get; set; }
    public string InterfaceType { get; set; } = "Ethernet";
    public long LinkSpeedBitsPerSec { get; set; }
    public long TimestampTicks { get; set; } = DateTime.UtcNow.Ticks;

    public byte[] ToBytes()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this);
    }

    public static DiscoveryBeacon? FromBytes(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<DiscoveryBeacon>(bytes);
        }
        catch
        {
            return null;
        }
    }
}
