using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.Network;

public class NetworkInterfaceDetector
{
    public event Action? NetworkChanged;

    public NetworkInterfaceDetector()
    {
        try
        {
            NetworkChange.NetworkAddressChanged += (_, _) => OnNetworkChanged();
            NetworkChange.NetworkAvailabilityChanged += (_, _) => OnNetworkChanged();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not register for network change events: {ex.Message}");
        }
    }

    private void OnNetworkChanged()
    {
        Logger.Info("Network configuration change detected.");
        NetworkChanged?.Invoke();
    }

    public List<NetworkAdapterInfo> GetAllAdapters()
    {
        var result = new List<NetworkAdapterInfo>();

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var nic in interfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var ipProps = nic.GetIPProperties();
                var unicast = ipProps.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);

                if (unicast == null)
                    continue;

                var info = new NetworkAdapterInfo
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    InterfaceType = nic.NetworkInterfaceType,
                    Status = nic.OperationalStatus,
                    SpeedBitsPerSec = nic.Speed,
                    IpAddress = unicast.Address,
                    SubnetMask = unicast.IPv4Mask
                };

                // Calculate broadcast address
                if (unicast.IPv4Mask != null)
                {
                    info.BroadcastAddress = CalculateBroadcastAddress(unicast.Address, unicast.IPv4Mask);
                }
                else
                {
                    info.BroadcastAddress = IPAddress.Broadcast;
                }

                // Identify if Virtual/VPN/Container
                info.IsVirtualOrVpn = IsVirtualOrVpnInterface(nic);
                info.IsWireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                info.IsPhysicalEthernet = (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                           nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet ||
                                           nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx ||
                                           nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT) &&
                                          !info.IsVirtualOrVpn && !info.IsWireless;

                result.Add(info);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to enumerate network interfaces", ex);
        }

        return result;
    }

    public NetworkAdapterInfo? GetBestEthernetAdapter(string? preferredId = null)
    {
        var adapters = GetAllAdapters();
        if (adapters.Count == 0) return null;

        // If user manually chose a preferred adapter in settings and it is currently active
        if (!string.IsNullOrEmpty(preferredId))
        {
            var preferred = adapters.FirstOrDefault(a => a.Id == preferredId);
            if (preferred != null) return preferred;
        }

        // 1. Prefer physical Ethernet with highest speed
        var physicalEthernet = adapters
            .Where(a => a.IsPhysicalEthernet)
            .OrderByDescending(a => a.SpeedBitsPerSec)
            .FirstOrDefault();

        if (physicalEthernet != null) return physicalEthernet;

        // 2. Fall back to non-virtual, non-wireless interfaces
        var otherWired = adapters
            .Where(a => !a.IsVirtualOrVpn && !a.IsWireless)
            .OrderByDescending(a => a.SpeedBitsPerSec)
            .FirstOrDefault();

        if (otherWired != null) return otherWired;

        // 3. Fall back to Wi-Fi if no wired connection exists
        var wireless = adapters
            .Where(a => a.IsWireless && !a.IsVirtualOrVpn)
            .OrderByDescending(a => a.SpeedBitsPerSec)
            .FirstOrDefault();

        if (wireless != null) return wireless;

        // 4. Fall back to any active adapter
        return adapters.OrderByDescending(a => a.SpeedBitsPerSec).FirstOrDefault();
    }

    private static bool IsVirtualOrVpnInterface(NetworkInterface nic)
    {
        string name = (nic.Name + " " + nic.Description).ToLowerInvariant();

        string[] virtualKeywords =
        [
            "virtual", "vbox", "vmware", "hyper-v", "docker", "wsl", "tap", "vpn",
            "zerotier", "tailscale", "wireguard", "hamachi", "openvpn", "nordlynx",
            "bluetooth", "loopback", "vethernet"
        ];

        return virtualKeywords.Any(k => name.Contains(k));
    }

    private static IPAddress CalculateBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        byte[] ipBytes = address.GetAddressBytes();
        byte[] maskBytes = subnetMask.GetAddressBytes();

        if (ipBytes.Length != maskBytes.Length)
            return IPAddress.Broadcast;

        byte[] broadcastBytes = new byte[ipBytes.Length];
        for (int i = 0; i < broadcastBytes.Length; i++)
        {
            broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
        }

        return new IPAddress(broadcastBytes);
    }
}
