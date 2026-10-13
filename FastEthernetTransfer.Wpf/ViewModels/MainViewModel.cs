using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Network;
using FastEthernetTransfer.Core.Storage;
using FastEthernetTransfer.Core.TransferEngine;
using FastEthernetTransfer.Wpf.Styles;
using Microsoft.Win32;

namespace FastEthernetTransfer.Wpf.ViewModels;

public enum NavSection
{
    Transfer,
    GameUpdater,
    Activity,
    Settings
}

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly NetworkInterfaceDetector _nicDetector;
    private readonly DiscoveryService _discoveryService;
    private readonly HighSpeedTcpReceiver _receiver;
    private readonly HistoryService _historyService = new();

    private HighSpeedTcpSender? _currentSender;
    private CancellationTokenSource? _transferCts;

    // Navigation
    private NavSection _currentNavSection = NavSection.Transfer;
    public NavSection CurrentNavSection
    {
        get => _currentNavSection;
        set => SetProperty(ref _currentNavSection, value);
    }

    // Theme
    private string _currentTheme = "Dark";
    public string CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (SetProperty(ref _currentTheme, value))
            {
                ThemeManager.ApplyTheme(value);
                _settingsService.Settings.AppTheme = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(IsDarkTheme));
            }
        }
    }
    public bool IsDarkTheme => string.Equals(CurrentTheme, "Dark", StringComparison.OrdinalIgnoreCase);

    // Local PC info
    private string _localDeviceName = Environment.MachineName;
    private string _localAdapterName = "Ethernet";
    private string _localLinkSpeed = "Checking...";
    private string _localIp = "127.0.0.1";
    private int _localPort = 45779;
    private bool _isLocalEthernetPhysical = true;

    // Discovered Peers
    public ObservableCollection<PeerInfo> OnlinePeers { get; } = [];
    private PeerInfo? _selectedPeer;

    // Drag-and-Drop & Selected Items Queue
    public ObservableCollection<SelectedItemViewModel> SelectedItems { get; } = [];
    private bool _isDragOver;
    public bool IsDragOver
    {
        get => _isDragOver;
        set => SetProperty(ref _isDragOver, value);
    }

    private string _selectedItemsSummary = "Drop files or folders here to begin";
    private bool _hasSelectedItems;
    private long _totalSelectedSizeBytes;

    // Transfer State
    private TransferProgress? _currentProgress;
    private bool _isTransferActive;
    private bool _isTransferPaused;
    private string _transferStatusMessage = "Ready for high-speed file transfer";

    // Incoming Request
    private IncomingTransferRequest? _pendingIncomingRequest;
    private bool _hasPendingIncomingRequest;
    private string _incomingDestinationFolder = string.Empty;
    private ConflictResolution _incomingConflictPolicy = ConflictResolution.Ask;

    // Activity & History
    public ObservableCollection<TransferHistoryItem> TransferHistory { get; } = [];
    public ObservableCollection<TransferProgress> RecentTransfers { get; } = [];

    // Sub-ViewModel for Game Updater
    public GameUpdaterViewModel GameUpdater { get; }

    // Settings
    public string SettingsDeviceName
    {
        get => _settingsService.Settings.DeviceName;
        set { _settingsService.Settings.DeviceName = value; OnPropertyChanged(); }
    }
    public int SettingsTcpPort
    {
        get => _settingsService.Settings.TcpPort;
        set { _settingsService.Settings.TcpPort = value; OnPropertyChanged(); }
    }
    public string SettingsDefaultDownloadPath
    {
        get => _settingsService.Settings.DefaultDownloadPath;
        set { _settingsService.Settings.DefaultDownloadPath = value; OnPropertyChanged(); }
    }
    public int SettingsBufferSizeBytes
    {
        get => _settingsService.Settings.BufferSizeBytes;
        set { _settingsService.Settings.BufferSizeBytes = value; OnPropertyChanged(); }
    }
    public VerificationMode SettingsVerificationMode
    {
        get => _settingsService.Settings.VerificationMode;
        set { _settingsService.Settings.VerificationMode = value; OnPropertyChanged(); }
    }
    public ConflictResolution SettingsConflictResolution
    {
        get => _settingsService.Settings.ConflictResolution;
        set { _settingsService.Settings.ConflictResolution = value; OnPropertyChanged(); }
    }
    public bool SettingsAutoAcceptTrustedPeers
    {
        get => _settingsService.Settings.AutoAcceptTrustedPeers;
        set { _settingsService.Settings.AutoAcceptTrustedPeers = value; OnPropertyChanged(); }
    }

    public static ConflictResolution[] AvailableConflictResolutions { get; } = Enum.GetValues<ConflictResolution>();
    public static VerificationMode[] AvailableVerificationModes { get; } = Enum.GetValues<VerificationMode>();

    public List<int> AvailableBufferSizes { get; } =
    [
        512 * 1024,
        1024 * 1024,
        2 * 1024 * 1024,
        4 * 1024 * 1024,
        8 * 1024 * 1024
    ];

    // Commands
    public ICommand NavigateCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand SelectFilesCommand { get; }
    public ICommand SelectFolderCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand RemoveSelectedItemCommand { get; }
    public ICommand SelectPeerCommand { get; }
    public ICommand StartTransferCommand { get; }
    public ICommand PauseTransferCommand { get; }
    public ICommand ResumeTransferCommand { get; }
    public ICommand CancelTransferCommand { get; }
    public ICommand AcceptIncomingCommand { get; }
    public ICommand DeclineIncomingCommand { get; }
    public ICommand BrowseIncomingFolderCommand { get; }
    public ICommand BrowseDownloadPathCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ConfigureFirewallCommand { get; }
    public ICommand ClearHistoryCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand RefreshNetworkCommand { get; }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _nicDetector = new NetworkInterfaceDetector();
        _discoveryService = new DiscoveryService(_settingsService, _nicDetector);
        _receiver = new HighSpeedTcpReceiver(_settingsService);

        _incomingDestinationFolder = _settingsService.Settings.DefaultDownloadPath;
        _currentTheme = _settingsService.Settings.AppTheme ?? "Dark";
        ThemeManager.ApplyTheme(_currentTheme);

        GameUpdater = new GameUpdaterViewModel(OnlinePeers, _settingsService, _nicDetector);

        // Load History
        LoadHistory();

        // Command bindings
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is NavSection section) CurrentNavSection = section;
            else if (p is string s && Enum.TryParse<NavSection>(s, true, out var parsed)) CurrentNavSection = parsed;
        });

        ToggleThemeCommand = new RelayCommand(OnToggleTheme);
        SelectFilesCommand = new RelayCommand(OnSelectFiles);
        SelectFolderCommand = new RelayCommand(OnSelectFolder);
        ClearSelectionCommand = new RelayCommand(OnClearSelection);
        RemoveSelectedItemCommand = new RelayCommand(p => RemoveSelectedItem(p as SelectedItemViewModel));
        SelectPeerCommand = new RelayCommand(p => SelectedPeer = p as PeerInfo);
        StartTransferCommand = new AsyncRelayCommand(OnStartTransferAsync, () => CanSend);
        PauseTransferCommand = new RelayCommand(OnPauseTransfer);
        ResumeTransferCommand = new RelayCommand(OnResumeTransfer);
        CancelTransferCommand = new RelayCommand(OnCancelTransfer);
        AcceptIncomingCommand = new RelayCommand(OnAcceptIncoming);
        DeclineIncomingCommand = new RelayCommand(OnDeclineIncoming);
        BrowseIncomingFolderCommand = new RelayCommand(OnBrowseIncomingFolder);
        BrowseDownloadPathCommand = new RelayCommand(OnBrowseDownloadPath);
        SaveSettingsCommand = new RelayCommand(OnSaveSettings);
        ConfigureFirewallCommand = new RelayCommand(OnConfigureFirewall);
        ClearHistoryCommand = new RelayCommand(OnClearHistory);
        OpenLogsCommand = new RelayCommand(Logger.OpenLogsFolder);
        RefreshNetworkCommand = new RelayCommand(RefreshNetworkInfo);

        // Events
        _discoveryService.PeerDiscovered += OnPeerDiscovered;
        _discoveryService.PeerUpdated += OnPeerUpdated;
        _discoveryService.PeerLost += OnPeerLost;

        _receiver.TransferRequested += OnTransferRequested;
        _receiver.ProgressChanged += OnProgressChanged;
        _receiver.GameUpdateFolderRequested += OnGameUpdateFolderRequested;

        RefreshNetworkInfo();
        _receiver.Start();
        _discoveryService.Start();
    }

    public string LocalDeviceName { get => _localDeviceName; set => SetProperty(ref _localDeviceName, value); }
    public string LocalAdapterName { get => _localAdapterName; set => SetProperty(ref _localAdapterName, value); }
    public string LocalLinkSpeed { get => _localLinkSpeed; set => SetProperty(ref _localLinkSpeed, value); }
    public string LocalIp { get => _localIp; set => SetProperty(ref _localIp, value); }
    public int LocalPort { get => _localPort; set => SetProperty(ref _localPort, value); }
    public bool IsLocalEthernetPhysical { get => _isLocalEthernetPhysical; set => SetProperty(ref _isLocalEthernetPhysical, value); }

    public PeerInfo? SelectedPeer
    {
        get => _selectedPeer;
        set
        {
            if (SetProperty(ref _selectedPeer, value))
            {
                OnPropertyChanged(nameof(CanSend));
            }
        }
    }

    public string SelectedItemsSummary { get => _selectedItemsSummary; set => SetProperty(ref _selectedItemsSummary, value); }
    public bool HasSelectedItems { get => _hasSelectedItems; set => SetProperty(ref _hasSelectedItems, value); }
    public long TotalSelectedSizeBytes
    {
        get => _totalSelectedSizeBytes;
        set
        {
            if (SetProperty(ref _totalSelectedSizeBytes, value))
            {
                OnPropertyChanged(nameof(TotalSelectedSizeFormatted));
            }
        }
    }
    public string TotalSelectedSizeFormatted => TransferProgress.FormatBytes(TotalSelectedSizeBytes);
    public bool CanSend => SelectedPeer != null && HasSelectedItems && !IsTransferActive;

    public TransferProgress? CurrentProgress { get => _currentProgress; set => SetProperty(ref _currentProgress, value); }
    public bool IsTransferActive { get => _isTransferActive; set { if (SetProperty(ref _isTransferActive, value)) OnPropertyChanged(nameof(CanSend)); } }
    public bool IsTransferPaused { get => _isTransferPaused; set => SetProperty(ref _isTransferPaused, value); }
    public string TransferStatusMessage { get => _transferStatusMessage; set => SetProperty(ref _transferStatusMessage, value); }

    public bool HasPendingIncomingRequest { get => _hasPendingIncomingRequest; set => SetProperty(ref _hasPendingIncomingRequest, value); }
    public IncomingTransferRequest? PendingIncomingRequest { get => _pendingIncomingRequest; set => SetProperty(ref _pendingIncomingRequest, value); }
    public string IncomingDestinationFolder { get => _incomingDestinationFolder; set => SetProperty(ref _incomingDestinationFolder, value); }
    public ConflictResolution IncomingConflictPolicy { get => _incomingConflictPolicy; set => SetProperty(ref _incomingConflictPolicy, value); }

    public SettingsService SettingsService => _settingsService;
    public NetworkInterfaceDetector NicDetector => _nicDetector;

    private void OnToggleTheme()
    {
        CurrentTheme = IsDarkTheme ? "Light" : "Dark";
    }

    public void HandleDroppedItems(IEnumerable<string>? droppedPaths)
    {
        var validatedItems = DragDropItemProcessor.ProcessDroppedPaths(droppedPaths);
        if (validatedItems.Count == 0) return;

        foreach (var item in validatedItems)
        {
            // Avoid duplicate additions
            if (SelectedItems.Any(existing => string.Equals(existing.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            SelectedItems.Add(new SelectedItemViewModel
            {
                FullPath = item.FullPath,
                Name = item.Name,
                IsDirectory = item.IsDirectory,
                SizeBytes = item.SizeBytes,
                SizeFormatted = TransferProgress.FormatBytes(item.SizeBytes)
            });
        }

        UpdateSelectedMetrics();
    }

    public void RemoveSelectedItem(SelectedItemViewModel? item)
    {
        if (item == null) return;
        SelectedItems.Remove(item);
        UpdateSelectedMetrics();
    }

    private void OnClearSelection()
    {
        SelectedItems.Clear();
        UpdateSelectedMetrics();
    }

    private void UpdateSelectedMetrics()
    {
        HasSelectedItems = SelectedItems.Count > 0;
        TotalSelectedSizeBytes = SelectedItems.Sum(i => i.SizeBytes);

        if (SelectedItems.Count == 0)
        {
            SelectedItemsSummary = "Drop files or folders here to begin";
        }
        else
        {
            int folderCount = SelectedItems.Count(i => i.IsDirectory);
            int fileCount = SelectedItems.Count(i => !i.IsDirectory);
            var parts = new List<string>();
            if (fileCount > 0) parts.Add($"{fileCount} file(s)");
            if (folderCount > 0) parts.Add($"{folderCount} folder(s)");
            SelectedItemsSummary = $"{string.Join(", ", parts)} ({TotalSelectedSizeFormatted})";
        }

        OnPropertyChanged(nameof(CanSend));
    }

    private void OnSelectFiles()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select Files to Send"
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            HandleDroppedItems(dlg.FileNames);
        }
    }

    private void OnSelectFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select Folder to Send"
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
        {
            HandleDroppedItems([dlg.FolderName]);
        }
    }

    private async Task OnStartTransferAsync()
    {
        if (SelectedPeer == null || SelectedItems.Count == 0 || IsTransferActive)
            return;

        IsTransferActive = true;
        IsTransferPaused = false;
        _transferCts = new CancellationTokenSource();

        var startTime = DateTime.UtcNow;
        long totalTransferredBytes = 0;
        int totalFiles = 0;

        try
        {
            TransferStatusMessage = $"Preparing manifest for {SelectedPeer.DeviceName}...";

            var filePaths = SelectedItems.Where(i => !i.IsDirectory).Select(i => i.FullPath).ToList();
            var folderPaths = SelectedItems.Where(i => i.IsDirectory).Select(i => i.FullPath).ToList();

            var manifest = ManifestBuilder.BuildFromPaths(
                filePaths,
                folderPaths,
                _settingsService.Settings.DeviceId,
                _settingsService.Settings.DeviceName);

            totalTransferredBytes = manifest.TotalBytes;
            totalFiles = manifest.TotalFiles;

            // Build local path map
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifest.Items)
            {
                if (item.IsDirectory) continue;

                string? matched = filePaths.FirstOrDefault(p => Path.GetFileName(p) == item.RelativePath);
                if (matched != null)
                {
                    pathMap[item.RelativePath] = matched;
                }
                else
                {
                    foreach (var folder in folderPaths)
                    {
                        string folderName = new DirectoryInfo(folder).Name;
                        if (item.RelativePath.StartsWith(folderName + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            string relPart = item.RelativePath.Substring(folderName.Length + 1).Replace('/', Path.DirectorySeparatorChar);
                            string full = Path.Combine(folder, relPart);
                            if (File.Exists(full))
                            {
                                pathMap[item.RelativePath] = full;
                                break;
                            }
                        }
                    }
                }
            }

            _currentSender = new HighSpeedTcpSender(_settingsService.Settings);
            _currentSender.ProgressChanged += OnProgressChanged;

            TransferStatusMessage = $"Connecting to {SelectedPeer.DeviceName} ({SelectedPeer.IpAddress})...";

            bool success = await _currentSender.SendTransferAsync(
                SelectedPeer.IpAddress,
                SelectedPeer.Port,
                manifest,
                pathMap,
                _transferCts.Token);

            if (success)
            {
                TransferStatusMessage = "Transfer finished successfully!";
                RecordHistory(new TransferHistoryItem
                {
                    Direction = TransferDirection.Send,
                    PeerName = SelectedPeer.DeviceName,
                    TotalBytes = totalTransferredBytes,
                    TotalFiles = totalFiles,
                    State = TransferState.Completed,
                    AverageSpeedBytesPerSec = CalculateAvgSpeed(startTime, totalTransferredBytes),
                    TimestampUtc = DateTime.UtcNow
                });
                OnClearSelection();
            }
            else
            {
                TransferStatusMessage = "Transfer cancelled or interrupted.";
                RecordHistory(new TransferHistoryItem
                {
                    Direction = TransferDirection.Send,
                    PeerName = SelectedPeer.DeviceName,
                    TotalBytes = totalTransferredBytes,
                    TotalFiles = totalFiles,
                    State = TransferState.Cancelled,
                    AverageSpeedBytesPerSec = 0,
                    TimestampUtc = DateTime.UtcNow,
                    ErrorMessage = "User cancelled or peer disconnected"
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Send transfer error", ex);
            TransferStatusMessage = $"Transfer error: {ex.Message}";
            RecordHistory(new TransferHistoryItem
            {
                Direction = TransferDirection.Send,
                PeerName = SelectedPeer?.DeviceName ?? "Unknown",
                TotalBytes = totalTransferredBytes,
                TotalFiles = totalFiles,
                State = TransferState.Failed,
                AverageSpeedBytesPerSec = 0,
                TimestampUtc = DateTime.UtcNow,
                ErrorMessage = ex.Message
            });
        }
        finally
        {
            IsTransferActive = false;
            IsTransferPaused = false;
            _currentSender = null;
        }
    }

    private double CalculateAvgSpeed(DateTime startUtc, long bytes)
    {
        double elapsedSec = (DateTime.UtcNow - startUtc).TotalSeconds;
        return elapsedSec > 0.1 ? bytes / elapsedSec : 0;
    }

    private void OnPauseTransfer()
    {
        if (_currentSender != null && IsTransferActive && !IsTransferPaused)
        {
            _currentSender.Pause();
            IsTransferPaused = true;
            TransferStatusMessage = "Transfer paused";
        }
    }

    private void OnResumeTransfer()
    {
        if (_currentSender != null && IsTransferActive && IsTransferPaused)
        {
            _currentSender.Resume();
            IsTransferPaused = false;
            TransferStatusMessage = "Transfer resumed";
        }
    }

    private void OnCancelTransfer()
    {
        _transferCts?.Cancel();
        IsTransferActive = false;
        IsTransferPaused = false;
        TransferStatusMessage = "Transfer cancelled";
    }

    private void OnTransferRequested(IncomingTransferRequest request)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            PendingIncomingRequest = request;
            HasPendingIncomingRequest = true;

            if (_settingsService.Settings.AutoAcceptTrustedPeers &&
                _settingsService.Settings.TrustedPeerIds.Contains(request.SenderId))
            {
                OnAcceptIncoming();
            }
        });
    }

    private void OnAcceptIncoming()
    {
        if (PendingIncomingRequest == null) return;

        PendingIncomingRequest.DecisionSource.TrySetResult((true, IncomingDestinationFolder, IncomingConflictPolicy));
        HasPendingIncomingRequest = false;
        IsTransferActive = true;
        TransferStatusMessage = $"Receiving files from {PendingIncomingRequest.SenderDeviceName}...";
    }

    private void OnDeclineIncoming()
    {
        if (PendingIncomingRequest == null) return;

        PendingIncomingRequest.DecisionSource.TrySetResult((false, string.Empty, ConflictResolution.Ask));
        HasPendingIncomingRequest = false;
    }

    private void OnGameUpdateFolderRequested(FastEthernetTransfer.Core.GameUpdater.GameUpdateFolderRequest request)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var result = MessageBox.Show(
                $"PC '{request.RequesterDeviceName}' wants to update its game installation using your computer as the source.\n\nClick 'Yes' to select your game folder, or 'No' to decline.",
                "🎮 Game Updater - Source Selection Request",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var dlg = new OpenFolderDialog
                {
                    Title = $"Select Game Directory for {request.RequesterDeviceName}"
                };

                if (dlg.ShowDialog() == true && Directory.Exists(dlg.FolderName))
                {
                    request.DecisionSource.TrySetResult((true, dlg.FolderName));
                    TransferStatusMessage = $"Sharing game manifest with {request.RequesterDeviceName}...";
                    return;
                }
            }

            request.DecisionSource.TrySetResult((false, null));
        });
    }

    private void OnBrowseIncomingFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose Destination Folder",
            InitialDirectory = IncomingDestinationFolder
        };

        if (dlg.ShowDialog() == true)
        {
            IncomingDestinationFolder = dlg.FolderName;
        }
    }

    private void OnBrowseDownloadPath()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose Default Download Folder",
            InitialDirectory = SettingsDefaultDownloadPath
        };

        if (dlg.ShowDialog() == true)
        {
            SettingsDefaultDownloadPath = dlg.FolderName;
        }
    }

    private void OnSaveSettings()
    {
        _settingsService.Save();
        TransferStatusMessage = "Settings saved successfully.";
        MessageBox.Show("Settings saved successfully.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnConfigureFirewall()
    {
        bool success = FirewallHelper.AddFirewallRule(out var msg);
        MessageBox.Show(
            success ? "Windows Firewall rule configured successfully!" : $"Could not configure firewall rule automatically:\n{msg}",
            "Firewall Configuration",
            MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OnProgressChanged(TransferProgress progress)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CurrentProgress = progress;

            if (progress.State == TransferState.Completed)
            {
                IsTransferActive = false;
                TransferStatusMessage = "Transfer completed!";
                AddToRecent(progress);
                RecordHistory(new TransferHistoryItem
                {
                    Direction = progress.Direction,
                    PeerName = progress.PeerName,
                    TotalBytes = progress.TotalBytes,
                    TotalFiles = progress.TotalFiles,
                    State = TransferState.Completed,
                    AverageSpeedBytesPerSec = progress.AverageSpeedBytesPerSec,
                    TimestampUtc = DateTime.UtcNow
                });
            }
            else if (progress.State == TransferState.Failed || progress.State == TransferState.Cancelled)
            {
                IsTransferActive = false;
                TransferStatusMessage = progress.ErrorMessage ?? (progress.State == TransferState.Cancelled ? "Cancelled" : "Failed");
                AddToRecent(progress);
                RecordHistory(new TransferHistoryItem
                {
                    Direction = progress.Direction,
                    PeerName = progress.PeerName,
                    TotalBytes = progress.TotalBytes,
                    TotalFiles = progress.TotalFiles,
                    State = progress.State,
                    AverageSpeedBytesPerSec = progress.AverageSpeedBytesPerSec,
                    TimestampUtc = DateTime.UtcNow,
                    ErrorMessage = progress.ErrorMessage
                });
            }
        });
    }

    private void AddToRecent(TransferProgress p)
    {
        RecentTransfers.Insert(0, p);
        while (RecentTransfers.Count > 20)
        {
            RecentTransfers.RemoveAt(RecentTransfers.Count - 1);
        }
    }

    private void LoadHistory()
    {
        TransferHistory.Clear();
        foreach (var item in _historyService.LoadHistory())
        {
            TransferHistory.Add(item);
        }
    }

    private void RecordHistory(TransferHistoryItem item)
    {
        _historyService.AddItem(item);
        TransferHistory.Insert(0, item);
        while (TransferHistory.Count > 100)
        {
            TransferHistory.RemoveAt(TransferHistory.Count - 1);
        }
    }

    private void OnClearHistory()
    {
        _historyService.ClearHistory();
        TransferHistory.Clear();
        RecentTransfers.Clear();
    }

    private void OnPeerDiscovered(PeerInfo peer)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = OnlinePeers.FirstOrDefault(p => p.Id == peer.Id);
            if (existing == null)
            {
                OnlinePeers.Add(peer);
                if (SelectedPeer == null)
                {
                    SelectedPeer = peer;
                }
            }
        });
    }

    private void OnPeerUpdated(PeerInfo peer)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = OnlinePeers.FirstOrDefault(p => p.Id == peer.Id);
            if (existing != null)
            {
                existing.LastSeenUtc = peer.LastSeenUtc;
                existing.LinkSpeedBitsPerSec = peer.LinkSpeedBitsPerSec;
                existing.DeviceName = peer.DeviceName;
            }
        });
    }

    private void OnPeerLost(PeerInfo peer)
    {
        Guid peerId = peer.Id;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = OnlinePeers.FirstOrDefault(p => p.Id == peerId);
            if (existing != null)
            {
                OnlinePeers.Remove(existing);
                if (SelectedPeer?.Id == peerId)
                {
                    SelectedPeer = OnlinePeers.FirstOrDefault();
                }
            }
        });
    }

    public void RefreshNetworkInfo()
    {
        var bestNic = _nicDetector.GetBestEthernetAdapter(_settingsService.Settings.PreferredNetworkInterfaceId);
        if (bestNic != null)
        {
            LocalAdapterName = bestNic.Name;
            LocalLinkSpeed = bestNic.SpeedFormatted;
            LocalIp = bestNic.IpAddress?.ToString() ?? "Unknown";
            IsLocalEthernetPhysical = bestNic.IsPhysicalEthernet;
        }
        else
        {
            LocalAdapterName = "No Ethernet Found";
            LocalLinkSpeed = "0 Mbps";
            LocalIp = "127.0.0.1";
            IsLocalEthernetPhysical = false;
        }

        LocalDeviceName = _settingsService.Settings.DeviceName;
        LocalPort = _settingsService.Settings.TcpPort;
    }

    public void Dispose()
    {
        _transferCts?.Cancel();
        _transferCts?.Dispose();
        _receiver.Dispose();
        _discoveryService.Dispose();
        GameUpdater.Dispose();
    }
}
