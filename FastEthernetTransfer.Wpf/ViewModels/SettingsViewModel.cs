using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Storage;
using Microsoft.Win32;

namespace FastEthernetTransfer.Wpf.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;

    private string _deviceName;
    private int _tcpPort;
    private string _defaultDownloadPath;
    private int _bufferSizeBytes;
    private VerificationMode _verificationMode;
    private ConflictResolution _conflictResolution;
    private bool _autoAcceptTrustedPeers;
    private string _firewallStatus = "Check or configure Windows Firewall rule";

    public List<int> AvailableBufferSizes { get; } =
    [
        512 * 1024,       // 512 KB
        1024 * 1024,      // 1 MB
        2 * 1024 * 1024,  // 2 MB (Default)
        4 * 1024 * 1024,  // 4 MB (High-Throughput 2.5G/10G)
        8 * 1024 * 1024   // 8 MB (Ultra 10G+)
    ];

    public ICommand BrowseDownloadPathCommand { get; }
    public ICommand ConfigureFirewallCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public event Action? RequestClose;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Settings;

        _deviceName = _settings.DeviceName;
        _tcpPort = _settings.TcpPort;
        _defaultDownloadPath = _settings.DefaultDownloadPath;
        _bufferSizeBytes = _settings.BufferSizeBytes;
        _verificationMode = _settings.VerificationMode;
        _conflictResolution = _settings.ConflictResolution;
        _autoAcceptTrustedPeers = _settings.AutoAcceptTrustedPeers;

        BrowseDownloadPathCommand = new RelayCommand(OnBrowseDownloadPath);
        ConfigureFirewallCommand = new RelayCommand(OnConfigureFirewall);
        OpenLogsCommand = new RelayCommand(Logger.OpenLogsFolder);
        SaveSettingsCommand = new RelayCommand(OnSave);
    }

    public string DeviceName { get => _deviceName; set => SetProperty(ref _deviceName, value); }
    public int TcpPort { get => _tcpPort; set => SetProperty(ref _tcpPort, value); }
    public string DefaultDownloadPath { get => _defaultDownloadPath; set => SetProperty(ref _defaultDownloadPath, value); }
    public int BufferSizeBytes { get => _bufferSizeBytes; set => SetProperty(ref _bufferSizeBytes, value); }
    public VerificationMode VerificationMode { get => _verificationMode; set => SetProperty(ref _verificationMode, value); }
    public ConflictResolution ConflictResolution { get => _conflictResolution; set => SetProperty(ref _conflictResolution, value); }
    public bool AutoAcceptTrustedPeers { get => _autoAcceptTrustedPeers; set => SetProperty(ref _autoAcceptTrustedPeers, value); }
    public string FirewallStatus { get => _firewallStatus; set => SetProperty(ref _firewallStatus, value); }

    public string FormatBufferSize(int bytes)
    {
        return $"{bytes / (1024 * 1024)} MB";
    }

    private void OnBrowseDownloadPath()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose Default Download Folder",
            InitialDirectory = DefaultDownloadPath
        };

        if (dlg.ShowDialog() == true)
        {
            DefaultDownloadPath = dlg.FolderName;
        }
    }

    private void OnConfigureFirewall()
    {
        bool success = FirewallHelper.AddFirewallRule(out string msg);
        FirewallStatus = success ? "✓ Firewall rule active" : $"Notice: {msg}";
    }

    private void OnSave()
    {
        _settings.DeviceName = DeviceName;
        _settings.TcpPort = TcpPort;
        _settings.DefaultDownloadPath = DefaultDownloadPath;
        _settings.BufferSizeBytes = BufferSizeBytes;
        _settings.VerificationMode = VerificationMode;
        _settings.ConflictResolution = ConflictResolution;
        _settings.AutoAcceptTrustedPeers = AutoAcceptTrustedPeers;

        _settingsService.Save(_settings);
        RequestClose?.Invoke();
    }
}
