using System;
using System.Collections.Generic;
using System.IO;

namespace FastEthernetTransfer.Core.Models;

public class AppSettings
{
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public string DeviceName { get; set; } = Environment.MachineName;
    public int TcpPort { get; set; } = 45779;
    public int DiscoveryPort { get; set; } = 45778;
    public string MulticastGroup { get; set; } = "239.255.43.21";

    public string DefaultDownloadPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "FastEthernetTransfer");

    // Configurable buffer size for maximum Ethernet throughput
    // Defaults to 2 MB (2 * 1024 * 1024), configurable from 256KB to 8MB
    public int BufferSizeBytes { get; set; } = 2 * 1024 * 1024;

    public VerificationMode VerificationMode { get; set; } = VerificationMode.Always;
    public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.Ask;
    public bool AutoAcceptTrustedPeers { get; set; } = false;
    public HashSet<Guid> TrustedPeerIds { get; set; } = [];

    public string? PreferredNetworkInterfaceId { get; set; }
    public string AppTheme { get; set; } = "Dark";
}
