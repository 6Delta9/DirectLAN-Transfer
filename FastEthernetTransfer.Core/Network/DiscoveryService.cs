using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Storage;

namespace FastEthernetTransfer.Core.Network;

public class DiscoveryService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly NetworkInterfaceDetector _nicDetector;

    private UdpClient? _multicastReceiver;
    private UdpClient? _broadcastReceiver;
    private UdpClient? _senderClient;

    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<Guid, PeerInfo> _peers = new();
    private readonly Lock _lock = new();

    public event Action<PeerInfo>? PeerDiscovered;
    public event Action<PeerInfo>? PeerUpdated;
    public event Action<PeerInfo>? PeerLost;

    public IReadOnlyCollection<PeerInfo> DiscoveredPeers => _peers.Values.ToList();

    public DiscoveryService(SettingsService settingsService, NetworkInterfaceDetector nicDetector)
    {
        _settingsService = settingsService;
        _settings = settingsService.Settings;
        _nicDetector = nicDetector;
        _nicDetector.NetworkChanged += RestartDiscovery;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            InitializeSockets();

            Task.Run(() => BroadcastLoopAsync(token), token);
            Task.Run(() => ReceiveMulticastLoopAsync(token), token);
            Task.Run(() => ReceiveBroadcastLoopAsync(token), token);
            Task.Run(() => PeerEvictionLoopAsync(token), token);

            Logger.Info($"DiscoveryService started on port {_settings.DiscoveryPort}");
        }
    }

    private void InitializeSockets()
    {
        try
        {
            CloseSockets();

            int port = _settings.DiscoveryPort;
            var multicastIp = IPAddress.Parse(_settings.MulticastGroup);

            // Multicast receiver
            _multicastReceiver = new UdpClient();
            _multicastReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _multicastReceiver.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            try
            {
                _multicastReceiver.JoinMulticastGroup(multicastIp);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to join multicast group {multicastIp}: {ex.Message}");
            }

            // Broadcast receiver on port + 1 to avoid port collision if needed, or reuse
            _broadcastReceiver = new UdpClient();
            _broadcastReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _broadcastReceiver.Client.Bind(new IPEndPoint(IPAddress.Any, port + 1));

            // Sender
            _senderClient = new UdpClient();
            _senderClient.EnableBroadcast = true;
            _senderClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Error initializing discovery sockets", ex);
        }
    }

    private void CloseSockets()
    {
        try { _multicastReceiver?.Dispose(); } catch { }
        try { _broadcastReceiver?.Dispose(); } catch { }
        try { _senderClient?.Dispose(); } catch { }
        _multicastReceiver = null;
        _broadcastReceiver = null;
        _senderClient = null;
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_cts == null) return;
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
            CloseSockets();
            Logger.Info("DiscoveryService stopped.");
        }
    }

    public void RestartDiscovery()
    {
        Logger.Info("Restarting discovery service due to network change...");
        Stop();
        Start();
    }

    private async Task BroadcastLoopAsync(CancellationToken token)
    {
        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(_settings.MulticastGroup), _settings.DiscoveryPort);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var bestNic = _nicDetector.GetBestEthernetAdapter(_settings.PreferredNetworkInterfaceId);

                var beacon = new DiscoveryBeacon
                {
                    ProtocolVersion = 1,
                    DeviceId = _settings.DeviceId,
                    DeviceName = _settings.DeviceName,
                    TcpPort = _settings.TcpPort,
                    InterfaceType = bestNic?.IsPhysicalEthernet == true ? "Ethernet" : (bestNic?.IsWireless == true ? "Wi-Fi" : "Network"),
                    LinkSpeedBitsPerSec = bestNic?.SpeedBitsPerSec ?? 0
                };

                byte[] data = beacon.ToBytes();

                if (_senderClient != null)
                {
                    // 1. Send via Multicast
                    try
                    {
                        await _senderClient.SendAsync(data, data.Length, multicastEndpoint);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Multicast send warning: {ex.Message}");
                    }

                    // 2. Send via Broadcast fallback
                    if (bestNic?.BroadcastAddress != null)
                    {
                        try
                        {
                            var broadcastEndpoint = new IPEndPoint(bestNic.BroadcastAddress, _settings.DiscoveryPort + 1);
                            await _senderClient.SendAsync(data, data.Length, broadcastEndpoint);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Subnet broadcast send warning: {ex.Message}");
                        }
                    }

                    // Also general broadcast
                    try
                    {
                        var generalBroadcast = new IPEndPoint(IPAddress.Broadcast, _settings.DiscoveryPort + 1);
                        await _senderClient.SendAsync(data, data.Length, generalBroadcast);
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Broadcast loop error: {ex.Message}");
            }

            try
            {
                await Task.Delay(1500, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveMulticastLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _multicastReceiver != null)
        {
            try
            {
                var result = await _multicastReceiver.ReceiveAsync(token);
                ProcessBeacon(result.Buffer, result.RemoteEndPoint.Address.ToString());
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Logger.Warn($"ReceiveMulticast error: {ex.Message}");
                    await Task.Delay(500, token);
                }
            }
        }
    }

    private async Task ReceiveBroadcastLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _broadcastReceiver != null)
        {
            try
            {
                var result = await _broadcastReceiver.ReceiveAsync(token);
                ProcessBeacon(result.Buffer, result.RemoteEndPoint.Address.ToString());
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Logger.Warn($"ReceiveBroadcast error: {ex.Message}");
                    await Task.Delay(500, token);
                }
            }
        }
    }

    private void ProcessBeacon(byte[] buffer, string remoteIp)
    {
        var beacon = DiscoveryBeacon.FromBytes(buffer);
        if (beacon == null) return;

        // Ignore beacons from ourselves
        if (beacon.DeviceId == _settings.DeviceId) return;

        bool isNew = false;
        var peer = _peers.AddOrUpdate(beacon.DeviceId,
            addValueFactory: id =>
            {
                isNew = true;
                return new PeerInfo
                {
                    Id = id,
                    DeviceName = beacon.DeviceName,
                    IpAddress = remoteIp,
                    Port = beacon.TcpPort,
                    InterfaceType = beacon.InterfaceType,
                    LinkSpeedBitsPerSec = beacon.LinkSpeedBitsPerSec,
                    LastSeenUtc = DateTime.UtcNow,
                    IsTrusted = _settingsService.IsPeerTrusted(id),
                    ProtocolVersion = beacon.ProtocolVersion
                };
            },
            updateValueFactory: (id, existing) =>
            {
                existing.DeviceName = beacon.DeviceName;
                existing.IpAddress = remoteIp;
                existing.Port = beacon.TcpPort;
                existing.InterfaceType = beacon.InterfaceType;
                existing.LinkSpeedBitsPerSec = beacon.LinkSpeedBitsPerSec;
                existing.LastSeenUtc = DateTime.UtcNow;
                existing.IsTrusted = _settingsService.IsPeerTrusted(id);
                existing.ProtocolVersion = beacon.ProtocolVersion;
                return existing;
            });

        if (isNew)
        {
            Logger.Info($"Discovered peer: {peer.DeviceName} ({peer.IpAddress}:{peer.Port} - {peer.LinkSpeedFormatted})");
            PeerDiscovered?.Invoke(peer);
        }
        else
        {
            PeerUpdated?.Invoke(peer);
        }
    }

    private async Task PeerEvictionLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, token);

                var now = DateTime.UtcNow;
                var timeoutThreshold = TimeSpan.FromSeconds(6);

                foreach (var kvp in _peers.ToList())
                {
                    if (now - kvp.Value.LastSeenUtc > timeoutThreshold)
                    {
                        if (_peers.TryRemove(kvp.Key, out var expiredPeer))
                        {
                            Logger.Info($"Peer timed out/offline: {expiredPeer.DeviceName} ({expiredPeer.IpAddress})");
                            PeerLost?.Invoke(expiredPeer);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Peer eviction error: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _nicDetector.NetworkChanged -= RestartDiscovery;
        Stop();
    }
}
