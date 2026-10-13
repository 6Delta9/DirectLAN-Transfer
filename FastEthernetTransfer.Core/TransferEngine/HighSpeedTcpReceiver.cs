using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Storage;

namespace FastEthernetTransfer.Core.TransferEngine;

public class IncomingTransferRequest
{
    public Guid TransferId { get; set; }
    public string SenderDeviceName { get; set; } = string.Empty;
    public Guid SenderId { get; set; }
    public string SenderIp { get; set; } = string.Empty;
    public TransferManifest Manifest { get; set; } = new();
    public long RequiredBytes => Manifest.TotalBytes;
    public long AvailableDiskBytes { get; set; }
    public bool HasEnoughSpace => AvailableDiskBytes >= RequiredBytes;

    public TaskCompletionSource<(bool Accepted, string DestinationFolder, ConflictResolution ConflictPolicy)> DecisionSource { get; } = new();
}

public class HighSpeedTcpReceiver : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly TransferSpeedTracker _speedTracker = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private volatile bool _isDisposed = false;

    public event Action<IncomingTransferRequest>? TransferRequested;
    public event Action<TransferProgress>? ProgressChanged;
    public event Action<FastEthernetTransfer.Core.GameUpdater.GameUpdateFolderRequest>? GameUpdateFolderRequested;

    public HighSpeedTcpReceiver(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Settings;
    }

    public int BoundPort { get; private set; }

    public bool Start()
    {
        if (_listener != null) return true;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _settings.TcpPort);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start(10);
            BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            Logger.Info($"HighSpeedTcpReceiver listening on port {BoundPort}");
            Task.Run(() => AcceptClientsLoopAsync(_cts.Token));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to start TCP listener on port {_settings.TcpPort}", ex);
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
        }
        catch { }
        finally
        {
            _listener = null;
        }
    }

    private async Task AcceptClientsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleIncomingClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Logger.Warn($"Accept client error: {ex.Message}");
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task HandleIncomingClientAsync(TcpClient client, CancellationToken ct)
    {
        using var tcpClient = client;
        tcpClient.NoDelay = true;
        int bufferSize = Math.Clamp(_settings.BufferSizeBytes, 256 * 1024, 8 * 1024 * 1024);
        tcpClient.ReceiveBufferSize = bufferSize;
        tcpClient.SendBufferSize = bufferSize;

        using var stream = tcpClient.GetStream();
        string remoteEndpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        string remoteIp = ((IPEndPoint?)tcpClient.Client.RemoteEndPoint)?.Address.ToString() ?? "Unknown";

        Guid currentTransferId = Guid.Empty;
        string senderName = "Remote PC";

        try
        {
            // 1. Read Handshake
            var (handshakeType, handshakeLen) = await TransferProtocol.ReadHeaderAsync(stream, ct).ConfigureAwait(false);
            if (handshakeType == FrameType.BenchmarkStart)
            {
                // Handle synthetic benchmark connection
                await HandleBenchmarkConnectionAsync(stream, handshakeLen, ct).ConfigureAwait(false);
                return;
            }

            if (handshakeType == FrameType.GameUpdateFolderRequest)
            {
                await HandleGameUpdateFolderRequestAsync(stream, handshakeLen, remoteIp, ct).ConfigureAwait(false);
                return;
            }

            if (handshakeType == FrameType.GameUpdateStartTransferRequest)
            {
                await HandleGameUpdateStartTransferRequestAsync(stream, handshakeLen, remoteIp, ct).ConfigureAwait(false);
                return;
            }

            if (handshakeType != FrameType.Handshake)
            {
                Logger.Warn($"Unexpected first frame: {handshakeType} from {remoteEndpoint}");
                return;
            }

            var handshake = await TransferProtocol.ReadJsonPayloadAsync<HandshakePayload>(stream, handshakeLen, ct).ConfigureAwait(false);
            if (handshake == null) return;

            senderName = handshake.SenderDeviceName;

            // Acknowledge handshake
            var handshakeAck = new HandshakeAckPayload
            {
                Accepted = true,
                NegotiatedBufferSize = bufferSize
            };
            await TransferProtocol.WriteJsonFrameAsync(stream, FrameType.HandshakeAck, handshakeAck, ct).ConfigureAwait(false);

            // 2. Read Offer Manifest
            var (offerType, offerLen) = await TransferProtocol.ReadHeaderAsync(stream, ct).ConfigureAwait(false);
            if (offerType != FrameType.OfferManifest)
            {
                Logger.Warn($"Expected OfferManifest, got {offerType}");
                return;
            }

            var manifest = await TransferProtocol.ReadJsonPayloadAsync<TransferManifest>(stream, offerLen, ct).ConfigureAwait(false);
            if (manifest == null) return;

            currentTransferId = manifest.TransferId;
            _speedTracker.Start(manifest.TotalBytes, manifest.TotalFiles);

            // Check destination disk space
            string defaultFolder = _settings.DefaultDownloadPath;
            Directory.CreateDirectory(defaultFolder);
            long freeBytes = GetAvailableDiskSpace(defaultFolder);

            var request = new IncomingTransferRequest
            {
                TransferId = manifest.TransferId,
                SenderDeviceName = handshake.SenderDeviceName,
                SenderId = handshake.SenderId,
                SenderIp = remoteIp,
                Manifest = manifest,
                AvailableDiskBytes = freeBytes
            };

            // Prompt user or check auto-accept
            bool isTrusted = _settingsService.IsPeerTrusted(handshake.SenderId);
            bool autoAccept = _settings.AutoAcceptTrustedPeers && isTrusted && request.HasEnoughSpace;

            string targetFolder = defaultFolder;
            ConflictResolution conflictPolicy = _settings.ConflictResolution;

            if (autoAccept)
            {
                Logger.Info($"Auto-accepting transfer {manifest.TransferId} from trusted peer {handshake.SenderDeviceName}");
            }
            else
            {
                TransferRequested?.Invoke(request);
                var decision = await request.DecisionSource.Task.ConfigureAwait(false);
                if (!decision.Accepted)
                {
                    Logger.Info($"Transfer {manifest.TransferId} rejected by local user.");
                    var rejectResponse = new OfferResponsePayload { Accepted = false, Reason = "Declined by recipient." };
                    await TransferProtocol.WriteJsonFrameAsync(stream, FrameType.OfferResponse, rejectResponse, ct).ConfigureAwait(false);
                    return;
                }

                targetFolder = string.IsNullOrWhiteSpace(decision.DestinationFolder) ? defaultFolder : decision.DestinationFolder;
                conflictPolicy = decision.ConflictPolicy;
            }

            Directory.CreateDirectory(targetFolder);

            // Compute ResumeOffsets
            var resumeOffsets = CalculateResumeOffsets(manifest, targetFolder, conflictPolicy);

            var acceptResponse = new OfferResponsePayload
            {
                Accepted = true,
                DestinationFolder = targetFolder,
                ResumeOffsets = resumeOffsets
            };
            await TransferProtocol.WriteJsonFrameAsync(stream, FrameType.OfferResponse, acceptResponse, ct).ConfigureAwait(false);

            ReportProgress(currentTransferId, TransferState.Transferring, senderName);

            // 3. Receive File Stream Pipeline
            byte[] transferBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            long lastProgressReportTicks = Environment.TickCount64;

            try
            {
                // Active file state
                FileStream? currentFileStream = null;
                IncrementalHash? currentHash = null;
                TransferItem? currentItem = null;
                string? currentPartFilePath = null;
                string? currentFinalFilePath = null;

                bool transferActive = true;

                while (transferActive && !ct.IsCancellationRequested)
                {
                    var (frameType, frameLen) = await TransferProtocol.ReadHeaderAsync(stream, ct).ConfigureAwait(false);

                    switch (frameType)
                    {
                        case FrameType.FileStart:
                            {
                                byte[] startData = ArrayPool<byte>.Shared.Rent(frameLen);
                                try
                                {
                                    await TransferProtocol.ReadExactAsync(stream, startData.AsMemory(0, frameLen), ct).ConfigureAwait(false);
                                    int itemIndex = BinaryPrimitives.ReadInt32BigEndian(startData.AsSpan(0, 4));
                                    long resumeOffset = BinaryPrimitives.ReadInt64BigEndian(startData.AsSpan(4, 8));

                                    currentItem = manifest.Items.Find(i => i.ItemIndex == itemIndex);
                                    if (currentItem == null) break;

                                    string resolvedPath = Path.Combine(targetFolder, currentItem.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                                    if (currentItem.IsDirectory || resumeOffset == -1L)
                                    {
                                        // Directory creation
                                        Directory.CreateDirectory(resolvedPath);
                                        break;
                                    }

                                    // Resolve file path with conflict policy
                                    resolvedPath = ResolveConflict(resolvedPath, conflictPolicy, out bool shouldSkip);
                                    if (shouldSkip)
                                    {
                                        currentItem = null;
                                        break;
                                    }

                                    string? parentDir = Path.GetDirectoryName(resolvedPath);
                                    if (!string.IsNullOrEmpty(parentDir)) Directory.CreateDirectory(parentDir);

                                    currentFinalFilePath = resolvedPath;
                                    currentPartFilePath = resolvedPath + ".fetpart";

                                    _speedTracker.UpdateCurrentFile(itemIndex, Path.GetFileName(resolvedPath), currentItem.Size, resumeOffset);
                                    if (resumeOffset > 0)
                                    {
                                        _speedTracker.AddBytesTransferred(resumeOffset);
                                    }

                                    var fileOptions = new FileStreamOptions
                                    {
                                        Mode = resumeOffset > 0 ? FileMode.OpenOrCreate : FileMode.Create,
                                        Access = FileAccess.Write,
                                        Share = FileShare.None,
                                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                                        BufferSize = bufferSize
                                    };

                                    currentFileStream = new FileStream(currentPartFilePath, fileOptions);
                                    if (resumeOffset > 0)
                                    {
                                        currentFileStream.Seek(resumeOffset, SeekOrigin.Begin);
                                    }

                                    currentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(startData);
                                }
                                break;
                            }

                        case FrameType.DataChunk:
                            {
                                byte[] chunkHeader = ArrayPool<byte>.Shared.Rent(16);
                                try
                                {
                                    await TransferProtocol.ReadExactAsync(stream, chunkHeader.AsMemory(0, 16), ct).ConfigureAwait(false);
                                    int itemIndex = BinaryPrimitives.ReadInt32BigEndian(chunkHeader.AsSpan(0, 4));
                                    long offset = BinaryPrimitives.ReadInt64BigEndian(chunkHeader.AsSpan(4, 8));
                                    int dataLength = BinaryPrimitives.ReadInt32BigEndian(chunkHeader.AsSpan(12, 4));

                                    // Read data directly into transfer buffer
                                    int remainingToRead = dataLength;
                                    while (remainingToRead > 0)
                                    {
                                        int toRead = Math.Min(remainingToRead, bufferSize);
                                        await TransferProtocol.ReadExactAsync(stream, transferBuffer.AsMemory(0, toRead), ct).ConfigureAwait(false);

                                        if (currentFileStream != null)
                                        {
                                            await currentFileStream.WriteAsync(transferBuffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
                                            currentHash?.AppendData(transferBuffer, 0, toRead);
                                        }

                                        _speedTracker.AddBytesTransferred(toRead);
                                        remainingToRead -= toRead;

                                        long now = Environment.TickCount64;
                                        if (now - lastProgressReportTicks >= 100)
                                        {
                                            lastProgressReportTicks = now;
                                            ReportProgress(currentTransferId, TransferState.Transferring, senderName);
                                        }
                                    }
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(chunkHeader);
                                }
                                break;
                            }

                        case FrameType.FileEnd:
                            {
                                byte[] endData = ArrayPool<byte>.Shared.Rent(frameLen);
                                try
                                {
                                    await TransferProtocol.ReadExactAsync(stream, endData.AsMemory(0, frameLen), ct).ConfigureAwait(false);
                                    int itemIndex = BinaryPrimitives.ReadInt32BigEndian(endData.AsSpan(0, 4));
                                    string expectedSha256 = Encoding.UTF8.GetString(endData, 4, frameLen - 4).ToLowerInvariant();

                                    bool success = true;

                                    if (currentFileStream != null)
                                    {
                                        await currentFileStream.FlushAsync(ct).ConfigureAwait(false);
                                        currentFileStream.Dispose();
                                        currentFileStream = null;
                                    }

                                    if (currentHash != null)
                                    {
                                        byte[] computedBytes = currentHash.GetHashAndReset();
                                        currentHash.Dispose();
                                        currentHash = null;

                                        string computedSha256 = Convert.ToHexString(computedBytes).ToLowerInvariant();

                                        // Verify hash if not resumed or if matching
                                        if (!string.IsNullOrEmpty(expectedSha256) && expectedSha256 != computedSha256)
                                        {
                                            Logger.Warn($"Hash verification mismatch for item {itemIndex} ({computedSha256} != {expectedSha256})");
                                            success = false;
                                        }
                                    }

                                    // Commit part file to final file
                                    if (success && currentPartFilePath != null && currentFinalFilePath != null)
                                    {
                                        if (File.Exists(currentFinalFilePath))
                                        {
                                            File.Delete(currentFinalFilePath);
                                        }
                                        File.Move(currentPartFilePath, currentFinalFilePath, true);

                                        if (currentItem != null)
                                        {
                                            File.SetLastWriteTimeUtc(currentFinalFilePath, currentItem.LastWriteTimeUtc);
                                        }
                                    }

                                    // Send FileAck: [ItemIndex (4 bytes), Status (1 byte)]
                                    byte[] ackPayload = new byte[5];
                                    BinaryPrimitives.WriteInt32BigEndian(ackPayload.AsSpan(0, 4), itemIndex);
                                    ackPayload[4] = (byte)(success ? 0 : 1);
                                    await TransferProtocol.WriteFrameAsync(stream, FrameType.FileAck, ackPayload, ct).ConfigureAwait(false);

                                    _speedTracker.FileCompleted();
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(endData);
                                }
                                break;
                            }

                        case FrameType.TransferComplete:
                            {
                                transferActive = false;
                                ReportProgress(currentTransferId, TransferState.Completed, senderName);
                                Logger.Info($"Transfer {currentTransferId} completed successfully!");
                                break;
                            }

                        case FrameType.Cancel:
                            {
                                transferActive = false;
                                ReportProgress(currentTransferId, TransferState.Cancelled, senderName);
                                Logger.Info($"Transfer {currentTransferId} cancelled by sender.");
                                break;
                            }

                        default:
                            Logger.Warn($"Unknown frame type: {frameType}");
                            break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(transferBuffer);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"Receiver transfer {currentTransferId} cancelled.");
            ReportProgress(currentTransferId, TransferState.Cancelled, senderName);
        }
        catch (Exception ex)
        {
            Logger.Error($"Receiver error during transfer {currentTransferId}: {ex.Message}", ex);
            ReportProgress(currentTransferId, TransferState.Failed, senderName, ex.Message);
        }
    }

    private static Dictionary<int, long> CalculateResumeOffsets(TransferManifest manifest, string targetFolder, ConflictResolution conflictPolicy)
    {
        var result = new Dictionary<int, long>();

        if (conflictPolicy == ConflictResolution.Replace)
        {
            // If replace is chosen, do not resume partial files
            return result;
        }

        foreach (var item in manifest.Items)
        {
            if (item.IsDirectory) continue;

            string targetFile = Path.Combine(targetFolder, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string partFile = targetFile + ".fetpart";

            if (File.Exists(partFile))
            {
                long length = new FileInfo(partFile).Length;
                if (length > 0 && length < item.Size)
                {
                    result[item.ItemIndex] = length;
                }
            }
        }

        return result;
    }

    private static string ResolveConflict(string destinationPath, ConflictResolution policy, out bool shouldSkip)
    {
        shouldSkip = false;
        if (!File.Exists(destinationPath)) return destinationPath;

        switch (policy)
        {
            case ConflictResolution.Skip:
                shouldSkip = true;
                return destinationPath;

            case ConflictResolution.Rename:
                string dir = Path.GetDirectoryName(destinationPath) ?? "";
                string nameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
                string ext = Path.GetExtension(destinationPath);
                int counter = 1;
                string newPath;
                do
                {
                    newPath = Path.Combine(dir, $"{nameWithoutExt} ({counter++}){ext}");
                } while (File.Exists(newPath));
                return newPath;

            case ConflictResolution.Replace:
            case ConflictResolution.Ask:
            default:
                return destinationPath;
        }
    }

    private static long GetAvailableDiskSpace(string folderPath)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(folderPath)) ?? "C:\\";
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return long.MaxValue;
        }
    }

    private async Task HandleGameUpdateFolderRequestAsync(Stream stream, int initialLen, string remoteIp, CancellationToken ct)
    {
        var reqPayload = await TransferProtocol.ReadJsonPayloadAsync<FastEthernetTransfer.Core.GameUpdater.GameUpdateFolderRequestPayload>(stream, initialLen, ct).ConfigureAwait(false);
        if (reqPayload == null) return;

        var requestEvent = new FastEthernetTransfer.Core.GameUpdater.GameUpdateFolderRequest
        {
            RequestId = reqPayload.RequestId,
            RequesterDeviceName = reqPayload.RequesterDeviceName,
            RequesterIp = remoteIp
        };

        GameUpdateFolderRequested?.Invoke(requestEvent);
        var decision = await requestEvent.DecisionSource.Task.ConfigureAwait(false);

        var respPayload = new FastEthernetTransfer.Core.GameUpdater.GameUpdateFolderResponsePayload
        {
            RequestId = reqPayload.RequestId,
            Accepted = decision.accepted,
            SelectedFolderPath = decision.folderPath,
            Reason = decision.accepted ? null : "Declined by recipient"
        };

        await TransferProtocol.WriteJsonFrameAsync(stream, FrameType.GameUpdateFolderResponse, respPayload, ct).ConfigureAwait(false);

        if (!decision.accepted || string.IsNullOrEmpty(decision.folderPath))
            return;

        // Wait for manifest request
        var (type, len) = await TransferProtocol.ReadHeaderAsync(stream, ct).ConfigureAwait(false);
        if (type != FrameType.GameUpdateManifestRequest)
            return;

        var manifestReq = await TransferProtocol.ReadJsonPayloadAsync<FastEthernetTransfer.Core.GameUpdater.GameUpdateManifestRequestPayload>(stream, len, ct).ConfigureAwait(false);
        if (manifestReq == null) return;

        FastEthernetTransfer.Core.GameUpdater.FolderManifest? manifest = null;
        string? error = null;

        try
        {
            manifest = await FastEthernetTransfer.Core.GameUpdater.FolderManifest.ScanFolderAsync(decision.folderPath, null, ct, computeHashes: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        var manifestResp = new FastEthernetTransfer.Core.GameUpdater.GameUpdateManifestResponsePayload
        {
            RequestId = manifestReq.RequestId,
            Success = manifest != null,
            Manifest = manifest,
            Error = error
        };

        await TransferProtocol.WriteJsonFrameAsync(stream, FrameType.GameUpdateManifestResponse, manifestResp, ct).ConfigureAwait(false);
    }

    private async Task HandleGameUpdateStartTransferRequestAsync(Stream stream, int initialLen, string remoteIp, CancellationToken ct)
    {
        var req = await TransferProtocol.ReadJsonPayloadAsync<FastEthernetTransfer.Core.GameUpdater.GameUpdateStartTransferRequestPayload>(stream, initialLen, ct).ConfigureAwait(false);
        if (req == null) return;

        bool canProceed = Directory.Exists(req.SourceFolderPath) && req.RelativePathsToTransfer.Count > 0;
        var resp = new FastEthernetTransfer.Core.GameUpdater.GameUpdateStartTransferResponsePayload
        {
            RequestId = req.RequestId,
            Accepted = canProceed,
            Reason = canProceed ? null : "Source folder not found or no files specified."
        };

        await TransferProtocol.WriteJsonFrameAsync(stream, FrameType.GameUpdateStartTransferResponse, resp, ct).ConfigureAwait(false);

        if (!canProceed) return;

        // Spawn transfer in background using HighSpeedTcpSender
        _ = Task.Run(async () =>
        {
            try
            {
                var manifest = new TransferManifest
                {
                    TransferId = Guid.NewGuid(),
                    SenderId = _settings.DeviceId,
                    SenderDeviceName = _settings.DeviceName
                };

                var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var directorySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long totalBytes = 0;
                int fileIndex = 0;

                foreach (var relPath in req.RelativePathsToTransfer)
                {
                    string fullPath = Path.Combine(req.SourceFolderPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath))
                    {
                        var fi = new FileInfo(fullPath);
                        totalBytes += fi.Length;

                        string? dir = Path.GetDirectoryName(relPath.Replace('/', Path.DirectorySeparatorChar));
                        while (!string.IsNullOrEmpty(dir))
                        {
                            directorySet.Add(dir.Replace('\\', '/'));
                            dir = Path.GetDirectoryName(dir);
                        }

                        manifest.Items.Add(new TransferItem
                        {
                            ItemIndex = fileIndex++,
                            RelativePath = relPath,
                            Size = fi.Length,
                            IsDirectory = false,
                            LastWriteTimeUtc = fi.LastWriteTimeUtc
                        });

                        pathMap[relPath] = fullPath;
                    }
                }

                // Add directory markers
                int dirIndex = fileIndex;
                foreach (var dir in directorySet.OrderBy(d => d.Length))
                {
                    manifest.Items.Insert(0, new TransferItem
                    {
                        ItemIndex = dirIndex++,
                        RelativePath = dir,
                        Size = 0,
                        IsDirectory = true,
                        LastWriteTimeUtc = DateTime.UtcNow
                    });
                }

                manifest.TotalBytes = totalBytes;
                manifest.TotalFiles = pathMap.Count;
                manifest.TotalFolders = directorySet.Count;

                using var sender = new HighSpeedTcpSender(_settings);
                await sender.SendTransferAsync(req.TargetIp, req.TargetPort, manifest, pathMap, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error("Background GameUpdate transfer failed", ex);
            }
        });
    }

    private async Task HandleBenchmarkConnectionAsync(Stream stream, int initialLen, CancellationToken ct)
    {
        // Read BenchmarkStart payload
        byte[] buffer = ArrayPool<byte>.Shared.Rent(initialLen);
        try
        {
            await TransferProtocol.ReadExactAsync(stream, buffer.AsMemory(0, initialLen), ct).ConfigureAwait(false);
            long totalBytesToExpect = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(0, 8));

            byte[] recvBuffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
            try
            {
                long totalReceived = 0;
                while (totalReceived < totalBytesToExpect)
                {
                    var (type, len) = await TransferProtocol.ReadHeaderAsync(stream, ct).ConfigureAwait(false);
                    if (type != FrameType.BenchmarkData) break;

                    await TransferProtocol.ReadExactAsync(stream, recvBuffer.AsMemory(0, len), ct).ConfigureAwait(false);
                    totalReceived += len;
                }

                // Send BenchmarkEnd ACK
                await TransferProtocol.WriteFrameAsync(stream, FrameType.BenchmarkEnd, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(recvBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ReportProgress(Guid transferId, TransferState state, string peerName, string? error = null)
    {
        var progress = _speedTracker.GetProgress(transferId, TransferDirection.Receive, state, peerName, error);
        ProgressChanged?.Invoke(progress);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
