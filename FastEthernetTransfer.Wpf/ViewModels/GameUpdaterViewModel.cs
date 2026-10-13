using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.GameUpdater;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Network;
using FastEthernetTransfer.Core.Storage;
using FastEthernetTransfer.Core.TransferEngine;
using Microsoft.Win32;

namespace FastEthernetTransfer.Wpf.ViewModels;

public enum GameUpdaterState
{
    Setup,
    Scanning,
    Comparing,
    ReviewChanges,
    Transferring,
    Completed,
    Error
}

public class ComparedFileViewModel
{
    public string RelativePath { get; set; } = string.Empty;
    public string SizeFormatted { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public FileComparisonResult ResultType { get; set; }
    public string Result { get; set; } = string.Empty;
    public Brush ResultColor { get; set; } = Brushes.White;
    public string? ErrorMessage { get; set; }
}

public class GameUpdaterViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly NetworkInterfaceDetector _nicDetector;
    private readonly GameUpdateSession _session = new();
    private GameUpdateClient? _updateClient;
    private CancellationTokenSource? _cts;

    public ObservableCollection<PeerInfo> AvailablePeers { get; }

    private PeerInfo? _selectedPeer;
    public PeerInfo? SelectedPeer
    {
        get => _selectedPeer;
        set
        {
            if (SetProperty(ref _selectedPeer, value))
            {
                OnPropertyChanged(nameof(CanRequestRemoteFolder));
            }
        }
    }

    private bool _isFolderDropTargetActive;
    public bool IsFolderDropTargetActive
    {
        get => _isFolderDropTargetActive;
        set => SetProperty(ref _isFolderDropTargetActive, value);
    }

    public void HandleDroppedFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Directory.Exists(path))
        {
            LocalGameFolder = path;
        }
        else if (File.Exists(path))
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                LocalGameFolder = dir;
            }
        }
    }

    private string _localGameFolder = string.Empty;
    public string LocalGameFolder
    {
        get => _localGameFolder;
        set
        {
            if (SetProperty(ref _localGameFolder, value))
            {
                OnPropertyChanged(nameof(CanRequestRemoteFolder));
            }
        }
    }

    private string _remoteGameFolder = string.Empty;
    public string RemoteGameFolder
    {
        get => _remoteGameFolder;
        set => SetProperty(ref _remoteGameFolder, value);
    }

    private GameUpdaterState _currentState = GameUpdaterState.Setup;
    public GameUpdaterState CurrentState
    {
        get => _currentState;
        set => SetProperty(ref _currentState, value);
    }

    private string _statusMessage = "Select the remote source PC and your local destination game folder.";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private string _scanProgressText = string.Empty;
    public string ScanProgressText
    {
        get => _scanProgressText;
        set => SetProperty(ref _scanProgressText, value);
    }

    private int _scanProgressPercent = 0;
    public int ScanProgressPercent
    {
        get => _scanProgressPercent;
        set => SetProperty(ref _scanProgressPercent, value);
    }

    private bool _isIndeterminateProgress = false;
    public bool IsIndeterminateProgress
    {
        get => _isIndeterminateProgress;
        set => SetProperty(ref _isIndeterminateProgress, value);
    }

    // Comparison result lists
    private readonly List<ComparedFileViewModel> _allComparedFiles = [];
    public ObservableCollection<ComparedFileViewModel> FilteredFiles { get; } = [];

    private int _newFilesCount;
    public int NewFilesCount { get => _newFilesCount; set => SetProperty(ref _newFilesCount, value); }

    private int _changedFilesCount;
    public int ChangedFilesCount { get => _changedFilesCount; set => SetProperty(ref _changedFilesCount, value); }

    private int _identicalFilesCount;
    public int IdenticalFilesCount { get => _identicalFilesCount; set => SetProperty(ref _identicalFilesCount, value); }

    private int _destOnlyFilesCount;
    public int DestOnlyFilesCount { get => _destOnlyFilesCount; set => SetProperty(ref _destOnlyFilesCount, value); }

    private int _unreadableFilesCount;
    public int UnreadableFilesCount { get => _unreadableFilesCount; set => SetProperty(ref _unreadableFilesCount, value); }

    private long _totalTransferBytes;
    public long TotalTransferBytes
    {
        get => _totalTransferBytes;
        set
        {
            if (SetProperty(ref _totalTransferBytes, value))
            {
                OnPropertyChanged(nameof(TotalTransferSizeFormatted));
            }
        }
    }

    private int _totalTransferFiles;
    public int TotalTransferFiles { get => _totalTransferFiles; set => SetProperty(ref _totalTransferFiles, value); }

    public string TotalTransferSizeFormatted => TransferProgress.FormatBytes(TotalTransferBytes);

    private string _selectedFilter = "All Changes";
    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    private bool _removeDestOnlyFiles = false;
    public bool RemoveDestOnlyFiles
    {
        get => _removeDestOnlyFiles;
        set => SetProperty(ref _removeDestOnlyFiles, value);
    }

    private TransferProgress? _currentTransferProgress;
    public TransferProgress? CurrentTransferProgress
    {
        get => _currentTransferProgress;
        set => SetProperty(ref _currentTransferProgress, value);
    }

    private bool _isTransferring = false;
    public bool IsTransferring
    {
        get => _isTransferring;
        set => SetProperty(ref _isTransferring, value);
    }

    public bool CanRequestRemoteFolder => SelectedPeer != null && !string.IsNullOrWhiteSpace(LocalGameFolder);
    public bool CanStartUpdate => TotalTransferFiles > 0 && CurrentState == GameUpdaterState.ReviewChanges;

    // Commands
    public ICommand BrowseLocalFolderCommand { get; }
    public ICommand RequestRemoteFolderCommand { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand StartUpdateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BackToSetupCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand CloseCommand { get; }

    public event Action? RequestClose;

    public GameUpdaterViewModel(ObservableCollection<PeerInfo> peers, SettingsService settings, NetworkInterfaceDetector nicDetector)
    {
        AvailablePeers = peers;
        _settingsService = settings;
        _nicDetector = nicDetector;

        if (AvailablePeers.Count > 0)
        {
            SelectedPeer = AvailablePeers[0];
        }

        BrowseLocalFolderCommand = new RelayCommand(ExecuteBrowseLocalFolder);
        RequestRemoteFolderCommand = new AsyncRelayCommand(ExecuteRequestRemoteFolderAsync);
        SetFilterCommand = new RelayCommand(f => SelectedFilter = f?.ToString() ?? "All Changes");
        StartUpdateCommand = new AsyncRelayCommand(ExecuteStartUpdateAsync);
        CancelCommand = new RelayCommand(ExecuteCancel);
        BackToSetupCommand = new RelayCommand(ExecuteBackToSetup);
        RetryCommand = new RelayCommand(ExecuteRetry);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    private void ExecuteBrowseLocalFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select Destination Game Folder (to be updated)"
        };

        if (dlg.ShowDialog() == true)
        {
            LocalGameFolder = dlg.FolderName;
        }
    }

    private async Task ExecuteRequestRemoteFolderAsync()
    {
        if (SelectedPeer == null || string.IsNullOrWhiteSpace(LocalGameFolder)) return;

        if (!Directory.Exists(LocalGameFolder))
        {
            MessageBox.Show("Selected local destination game folder does not exist. Please select an existing directory or create it first.", "Folder Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CurrentState = GameUpdaterState.Scanning;
            StatusMessage = $"Connecting to {SelectedPeer.DeviceName} and requesting source folder approval...";
            ScanProgressText = "Waiting for remote PC user to select and approve the game folder...";
            IsIndeterminateProgress = true;

            _updateClient = new GameUpdateClient();
            var (accepted, remoteFolder, reason) = await _updateClient.RequestRemoteFolderAsync(
                SelectedPeer.IpAddress,
                SelectedPeer.Port,
                _settingsService.Settings.DeviceName,
                ct).ConfigureAwait(true);

            if (!accepted || string.IsNullOrEmpty(remoteFolder))
            {
                ErrorMessage = reason ?? "Request declined by remote peer.";
                CurrentState = GameUpdaterState.Error;
                return;
            }

            RemoteGameFolder = remoteFolder;
            StatusMessage = $"Remote folder selected: {remoteFolder}. Scanning manifests...";
            ScanProgressText = "Fetching remote source file manifest and hashes...";

            // 1. Fetch remote manifest
            var remoteManifest = await _updateClient.FetchRemoteManifestAsync(remoteFolder, ct).ConfigureAwait(true);
            _session.SourceManifest = remoteManifest;

            // 2. Scan local destination folder
            ScanProgressText = "Scanning local destination game folder...";
            IsIndeterminateProgress = false;
            var localScanProgress = new Progress<(int scanned, string currentFile)>(report =>
            {
                ScanProgressText = $"Scanning local: {report.scanned} files ({Path.GetFileName(report.currentFile)})";
            });

            await _session.ScanDestinationAsync(LocalGameFolder, localScanProgress, ct, computeHashes: false).ConfigureAwait(true);

            // 3. Staged comparison
            CurrentState = GameUpdaterState.Comparing;
            StatusMessage = "Comparing game files and computing SHA-256 hashes for same-sized files...";
            var compareProgress = new Progress<(int compared, int total, string currentFile)>(report =>
            {
                ScanProgressPercent = report.total > 0 ? (int)((double)report.compared / report.total * 100.0) : 0;
                ScanProgressText = $"Comparing [{report.compared}/{report.total}]: {report.currentFile}";
            });

            await _session.CompareAsync(compareProgress, ct).ConfigureAwait(true);

            // 4. Build results
            PopulateComparisonResults();
            CurrentState = GameUpdaterState.ReviewChanges;
            StatusMessage = $"Comparison complete: {TotalTransferFiles} file(s) to transfer ({TotalTransferSizeFormatted}).";
        }
        catch (OperationCanceledException)
        {
            CurrentState = GameUpdaterState.Setup;
            StatusMessage = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            Logger.Error("Game Updater scan/compare error", ex);
            ErrorMessage = $"Error during comparison: {ex.Message}";
            CurrentState = GameUpdaterState.Error;
        }
    }

    private void PopulateComparisonResults()
    {
        _allComparedFiles.Clear();

        int newCount = 0;
        int changedCount = 0;
        int identicalCount = 0;
        int destOnlyCount = 0;
        int unreadableCount = 0;
        long transferBytes = 0;
        int transferFiles = 0;

        foreach (var file in _session.ComparisonResults)
        {
            Brush color;
            string resultText;

            switch (file.Result)
            {
                case FileComparisonResult.New:
                    color = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Emerald
                    resultText = "New (Transfer)";
                    newCount++;
                    transferBytes += file.SourceSize;
                    transferFiles++;
                    break;
                case FileComparisonResult.Changed:
                    color = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Amber
                    resultText = "Changed (Transfer)";
                    changedCount++;
                    transferBytes += file.SourceSize;
                    transferFiles++;
                    break;
                case FileComparisonResult.Identical:
                    color = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")); // Cyan
                    resultText = "Identical (Skip)";
                    identicalCount++;
                    break;
                case FileComparisonResult.DestinationOnly:
                    color = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Rose
                    resultText = "Destination-Only (Keep)";
                    destOnlyCount++;
                    break;
                case FileComparisonResult.Unreadable:
                default:
                    color = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red
                    resultText = "Error / Locked";
                    unreadableCount++;
                    break;
            }
            color.Freeze();

            _allComparedFiles.Add(new ComparedFileViewModel
            {
                RelativePath = file.RelativePath,
                SizeBytes = file.SourceSize > 0 ? file.SourceSize : file.DestinationSize,
                SizeFormatted = file.SizeFormatted,
                ResultType = file.Result,
                Result = resultText,
                ResultColor = color,
                ErrorMessage = file.ErrorMessage
            });
        }

        NewFilesCount = newCount;
        ChangedFilesCount = changedCount;
        IdenticalFilesCount = identicalCount;
        DestOnlyFilesCount = destOnlyCount;
        UnreadableFilesCount = unreadableCount;
        TotalTransferBytes = transferBytes;
        TotalTransferFiles = transferFiles;

        OnPropertyChanged(nameof(CanStartUpdate));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredFiles.Clear();
        IEnumerable<ComparedFileViewModel> source = _allComparedFiles;

        switch (SelectedFilter)
        {
            case "New":
                source = _allComparedFiles.Where(f => f.ResultType == FileComparisonResult.New);
                break;
            case "Changed":
                source = _allComparedFiles.Where(f => f.ResultType == FileComparisonResult.Changed);
                break;
            case "Identical":
                source = _allComparedFiles.Where(f => f.ResultType == FileComparisonResult.Identical);
                break;
            case "Destination-Only":
                source = _allComparedFiles.Where(f => f.ResultType == FileComparisonResult.DestinationOnly);
                break;
            case "Errors":
                source = _allComparedFiles.Where(f => f.ResultType == FileComparisonResult.Unreadable);
                break;
            case "All Files":
                source = _allComparedFiles;
                break;
            case "All Changes":
            default:
                source = _allComparedFiles.Where(f => f.ResultType == FileComparisonResult.New || f.ResultType == FileComparisonResult.Changed);
                break;
        }

        foreach (var item in source)
        {
            FilteredFiles.Add(item);
        }
    }

    private async Task ExecuteStartUpdateAsync()
    {
        if (SelectedPeer == null || string.IsNullOrWhiteSpace(LocalGameFolder)) return;

        // Check if destination-only deletion is enabled
        if (RemoveDestOnlyFiles && DestOnlyFilesCount > 0)
        {
            var confirm = MessageBox.Show(
                $"Warning: You have enabled 'Remove files that do not exist in the source'.\n\nThis will permanently delete {DestOnlyFilesCount} file(s) from your destination folder.\n\nAre you sure you want to proceed with deletion?",
                "Confirm Deletion of Destination-Only Files",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                var deleted = GameUpdateSession.SafeDeleteDestinationOnlyFiles(LocalGameFolder, _session.DestOnlyFiles);
                Logger.Info($"Deleted {deleted.Count} destination-only files before update.");
            }
            else
            {
                RemoveDestOnlyFiles = false;
            }
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CurrentState = GameUpdaterState.Transferring;
            IsTransferring = true;
            StatusMessage = $"Requesting {SelectedPeer.DeviceName} to start streaming updated files...";

            var filesToTransfer = _session.ComparisonResults
                .Where(r => r.Result == FileComparisonResult.New || r.Result == FileComparisonResult.Changed)
                .Select(r => r.RelativePath)
                .ToList();

            if (filesToTransfer.Count == 0)
            {
                CurrentState = GameUpdaterState.Completed;
                StatusMessage = "All files are already up-to-date!";
                return;
            }

            var bestNic = _nicDetector.GetBestEthernetAdapter(_settingsService.Settings.PreferredNetworkInterfaceId);
            string myIp = bestNic?.IpAddress?.ToString() ?? "127.0.0.1";
            int myPort = _settingsService.Settings.TcpPort;

            _updateClient ??= new GameUpdateClient();
            bool startAccepted = await _updateClient.RequestStartTransferAsync(
                SelectedPeer.IpAddress,
                SelectedPeer.Port,
                myIp,
                myPort,
                RemoteGameFolder,
                LocalGameFolder,
                filesToTransfer,
                ct).ConfigureAwait(true);

            if (!startAccepted)
            {
                ErrorMessage = "Remote PC failed to start file streaming.";
                CurrentState = GameUpdaterState.Error;
                return;
            }

            StatusMessage = $"Updating game files from {SelectedPeer.DeviceName}...";
        }
        catch (OperationCanceledException)
        {
            CurrentState = GameUpdaterState.ReviewChanges;
            StatusMessage = "Update cancelled.";
        }
        catch (Exception ex)
        {
            Logger.Error("Game update start error", ex);
            ErrorMessage = $"Error starting update: {ex.Message}";
            CurrentState = GameUpdaterState.Error;
        }
    }

    private void ExecuteCancel()
    {
        _cts?.Cancel();
        if (CurrentState == GameUpdaterState.Scanning || CurrentState == GameUpdaterState.Comparing)
        {
            CurrentState = GameUpdaterState.Setup;
            StatusMessage = "Operation cancelled.";
        }
    }

    private void ExecuteBackToSetup()
    {
        _cts?.Cancel();
        CurrentState = GameUpdaterState.Setup;
        StatusMessage = "Ready to configure game update.";
    }

    private void ExecuteRetry()
    {
        CurrentState = GameUpdaterState.Setup;
        ErrorMessage = null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _updateClient?.Dispose();
    }
}
