using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Network;
using FastEthernetTransfer.Core.TransferEngine;
using Xunit;

namespace FastEthernetTransfer.Tests;

public class ProtocolAndBeaconTests
{
    [Fact]
    public void DiscoveryBeacon_SerializeAndDeserialize_MaintainsData()
    {
        var beacon = new DiscoveryBeacon
        {
            ProtocolVersion = 1,
            DeviceId = Guid.NewGuid(),
            DeviceName = "DESKTOP-ALPHA",
            TcpPort = 45779,
            InterfaceType = "Ethernet",
            LinkSpeedBitsPerSec = 2_500_000_000L
        };

        byte[] bytes = beacon.ToBytes();
        var deserialized = DiscoveryBeacon.FromBytes(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(beacon.DeviceId, deserialized.DeviceId);
        Assert.Equal(beacon.DeviceName, deserialized.DeviceName);
        Assert.Equal(beacon.TcpPort, deserialized.TcpPort);
        Assert.Equal("Ethernet", deserialized.InterfaceType);
        Assert.Equal(2_500_000_000L, deserialized.LinkSpeedBitsPerSec);
    }

    [Theory]
    [InlineData(10_000_000_000L, "10 Gbps")]
    [InlineData(5_000_000_000L, "5 Gbps")]
    [InlineData(2_500_000_000L, "2.5 Gbps")]
    [InlineData(1_000_000_000L, "1 Gbps")]
    [InlineData(100_000_000L, "100 Mbps")]
    public void PeerInfo_LinkSpeedFormatted_CorrectlyIdentifiesSpeed(long bps, string expectedFormatted)
    {
        var peer = new PeerInfo { LinkSpeedBitsPerSec = bps };
        Assert.Equal(expectedFormatted, peer.LinkSpeedFormatted);
    }

    [Fact]
    public async Task TransferProtocol_FrameHeader_RoundTrip_Works()
    {
        using var ms = new MemoryStream();
        byte[] payload = "Hello Transfer Engine"u8.ToArray();

        await TransferProtocol.WriteFrameAsync(ms, FrameType.Handshake, payload, CancellationToken.None);
        ms.Position = 0;

        var (type, length) = await TransferProtocol.ReadHeaderAsync(ms, CancellationToken.None);
        Assert.Equal(FrameType.Handshake, type);
        Assert.Equal(payload.Length, length);

        byte[] readBuffer = new byte[length];
        await TransferProtocol.ReadExactAsync(ms, readBuffer, CancellationToken.None);
        Assert.Equal(payload, readBuffer);
    }

    [Fact]
    public async Task TransferProtocol_InvalidMagic_ThrowsException()
    {
        using var ms = new MemoryStream();
        byte[] invalidHeader = [0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00];
        await ms.WriteAsync(invalidHeader);
        ms.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await TransferProtocol.ReadHeaderAsync(ms, CancellationToken.None);
        });
    }
}
