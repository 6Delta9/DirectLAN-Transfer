using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.TransferEngine;

public class HighSpeedTcpSender : IDisposable
{
    private readonly AppSettings _settings;
    private readonly TransferSpeedTracker _speedTracker = new();
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private readonly SemaphoreSlim _pauseLock = new(1, 1);
    private volatile bool _isPaused = false;
    private volatile bool _isDisposed = false;

    public event Action<TransferProgress>? ProgressChanged;

    public HighSpeedTcpSender(AppSettings settings)
    {
        _settings = settings;
    }

    public void Pause()
    {
        if (!_isPaused)
        {
            _isPaused = true;
            _pauseLock.Wait(0);
            Logger.Info("Transfer paused by sender.");
        }
    }

    public void Resume()
    {
        if (_isPaused)
        {
            _isPaused = false;
            try
            {
                _pauseLock.Release();
            }
            catch (SemaphoreFullException) { }
            Logger.Info("Transfer resumed by sender.");
        }
    }

    public async Task<bool> SendTransferAsync(
        string targetHost,
        int targetPort,
        TransferManifest manifest,
        Dictionary<string, string> localPathMap, // Maps RelativePath -> Full local file system path
        CancellationToken ct)
    {
        _speedTracker.Start(manifest.TotalBytes, manifest.TotalFiles);
        int bufferSize = Math.Clamp(_settings.BufferSizeBytes, 256 * 1024, 8 * 1024 * 1024);

        try
        {
            Logger.Info($"Connecting to {targetHost}:{targetPort} for transfer {manifest.TransferId} (Size: {manifest.TotalSizeFormatted})...");
            ReportProgress(manifest.TransferId, TransferState.Connecting, targetHost);

            _tcpClient = new TcpClient
            {
                NoDelay = true,
                SendBufferSize = bufferSize,
                ReceiveBufferSize = bufferSize
            };

            await _tcpClient.ConnectAsync(targetHost, targetPort, ct).ConfigureAwait(false);
            _networkStream = _tcpClient.GetStream();

            // 1. Send Handshake
            var handshake = new HandshakePayload
            {
                ProtocolVersion = 1,
                SenderId = _settings.DeviceId,
                SenderDeviceName = _settings.DeviceName,
                PreferredBufferSize = bufferSize
            };
            await TransferProtocol.WriteJsonFrameAsync(_networkStream, FrameType.Handshake, handshake, ct).ConfigureAwait(false);

            // Read HandshakeAck
            var (ackType, ackLen) = await TransferProtocol.ReadHeaderAsync(_networkStream, ct).ConfigureAwait(false);
            if (ackType != FrameType.HandshakeAck)
            {
                throw new InvalidOperationException($"Expected HandshakeAck, got {ackType}");
            }
            var handshakeAck = await TransferProtocol.ReadJsonPayloadAsync<HandshakeAckPayload>(_networkStream, ackLen, ct).ConfigureAwait(false);
            if (handshakeAck?.Accepted != true)
            {
                throw new InvalidOperationException($"Handshake rejected: {handshakeAck?.Reason ?? "Unknown"}");
            }

            // 2. Send Offer Manifest
            ReportProgress(manifest.TransferId, TransferState.Negotiating, targetHost);
            await TransferProtocol.WriteJsonFrameAsync(_networkStream, FrameType.OfferManifest, manifest, ct).ConfigureAwait(false);

            // Read OfferResponse
            var (offerRespType, offerRespLen) = await TransferProtocol.ReadHeaderAsync(_networkStream, ct).ConfigureAwait(false);
            if (offerRespType != FrameType.OfferResponse)
            {
                throw new InvalidOperationException($"Expected OfferResponse, got {offerRespType}");
            }
            var offerResponse = await TransferProtocol.ReadJsonPayloadAsync<OfferResponsePayload>(_networkStream, offerRespLen, ct).ConfigureAwait(false);
            if (offerResponse?.Accepted != true)
            {
                throw new InvalidOperationException($"Transfer rejected by recipient: {offerResponse?.Reason ?? "User declined"}");
            }

            var resumeOffsets = offerResponse.ResumeOffsets ?? [];

            // 3. Begin streaming items
            ReportProgress(manifest.TransferId, TransferState.Transferring, targetHost);
            byte[] transferBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            long lastProgressReportTicks = Environment.TickCount64;

            try
            {
                foreach (var item in manifest.Items)
                {
                    ct.ThrowIfCancellationRequested();

                    // Check pause
                    if (_isPaused)
                    {
                        ReportProgress(manifest.TransferId, TransferState.Paused, targetHost);
                        await _pauseLock.WaitAsync(ct).ConfigureAwait(false);
                        _pauseLock.Release();
                        ReportProgress(manifest.TransferId, TransferState.Transferring, targetHost);
                    }

                    if (item.IsDirectory)
                    {
                        // Folder creation marker: FileStart with ResumeOffset = -1
                        await SendDirectoryMarkerAsync(_networkStream, item.ItemIndex, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (!localPathMap.TryGetValue(item.RelativePath, out string? localFilePath) || !File.Exists(localFilePath))
                    {
                        Logger.Warn($"Source file not found: {item.RelativePath}");
                        continue;
                    }

                    long resumeOffset = 0;
                    if (resumeOffsets.TryGetValue(item.ItemIndex, out long offset) && offset > 0 && offset < item.Size)
                    {
                        resumeOffset = offset;
                        Logger.Info($"Resuming '{item.RelativePath}' from offset {resumeOffset}/{item.Size}");
                    }

                    _speedTracker.UpdateCurrentFile(item.ItemIndex, Path.GetFileName(localFilePath), item.Size, resumeOffset);
                    _speedTracker.AddBytesTransferred(resumeOffset);

                    // Send FileStart frame: [ItemIndex (4 bytes), ResumeOffset (8 bytes)]
                    await SendFileStartFrameAsync(_networkStream, item.ItemIndex, resumeOffset, ct).ConfigureAwait(false);

                    // Stream file chunks and calculate SHA-256 on the fly
                    using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                    var fileOptions = new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.Read,
                        Share = FileShare.Read,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                        BufferSize = bufferSize
                    };

                    using (var fileStream = new FileStream(localFilePath, fileOptions))
                    {
                        if (resumeOffset > 0)
                        {
                            // If resuming, read and hash the skipped portion if needed, or seek directly
                            // Seeking directly gives instant resume performance!
                            fileStream.Seek(resumeOffset, SeekOrigin.Begin);
                        }

                        long currentFileOffset = resumeOffset;
                        int bytesRead;

                        while ((bytesRead = await fileStream.ReadAsync(transferBuffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
                        {
                            if (_isPaused)
                            {
                                ReportProgress(manifest.TransferId, TransferState.Paused, targetHost);
                                await _pauseLock.WaitAsync(ct).ConfigureAwait(false);
                                _pauseLock.Release();
                                ReportProgress(manifest.TransferId, TransferState.Transferring, targetHost);
                            }

                            // Update SHA-256 hash stream
                            incrementalHash.AppendData(transferBuffer, 0, bytesRead);

                            // Send chunk via high-speed binary framing directly to socket
                            await TransferProtocol.WriteDataChunkAsync(
                                _networkStream,
                                item.ItemIndex,
                                currentFileOffset,
                                transferBuffer.AsMemory(0, bytesRead),
                                ct).ConfigureAwait(false);

                            currentFileOffset += bytesRead;
                            _speedTracker.AddBytesTransferred(bytesRead);

                            // Throttle progress report to UI (~10 Hz)
                            long now = Environment.TickCount64;
                            if (now - lastProgressReportTicks >= 100)
                            {
                                lastProgressReportTicks = now;
                                ReportProgress(manifest.TransferId, TransferState.Transferring, targetHost);
                            }
                        }
                    }

                    // Compute SHA-256 for remaining/streamed data
                    byte[] hashBytes = incrementalHash.GetHashAndReset();
                    string sha256Hex = Convert.ToHexString(hashBytes).ToLowerInvariant();

                    // Send FileEnd
                    await SendFileEndFrameAsync(_networkStream, item.ItemIndex, sha256Hex, ct).ConfigureAwait(false);

                    // Await FileAck
                    var (ackFrameType, ackFrameLen) = await TransferProtocol.ReadHeaderAsync(_networkStream, ct).ConfigureAwait(false);
                    if (ackFrameType != FrameType.FileAck)
                    {
                        throw new InvalidOperationException($"Expected FileAck, got {ackFrameType}");
                    }
                    byte[] ackData = ArrayPool<byte>.Shared.Rent(ackFrameLen);
                    try
                    {
                        await TransferProtocol.ReadExactAsync(_networkStream, ackData.AsMemory(0, ackFrameLen), ct).ConfigureAwait(false);
                        byte status = ackData[4]; // 0 = Success
                        if (status != 0)
                        {
                            Logger.Warn($"File verification failed on receiver for {item.RelativePath}");
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(ackData);
                    }

                    _speedTracker.FileCompleted();
                }

                // 4. Send TransferComplete
                await TransferProtocol.WriteFrameAsync(_networkStream, FrameType.TransferComplete, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
                ReportProgress(manifest.TransferId, TransferState.Completed, targetHost);
                Logger.Info($"Transfer {manifest.TransferId} finished successfully!");
                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(transferBuffer);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"Transfer {manifest.TransferId} was cancelled.");
            ReportProgress(manifest.TransferId, TransferState.Cancelled, targetHost);
            try
            {
                if (_networkStream != null)
                {
                    await TransferProtocol.WriteFrameAsync(_networkStream, FrameType.Cancel, ReadOnlyMemory<byte>.Empty, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch { }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"Transfer {manifest.TransferId} failed: {ex.Message}", ex);
            ReportProgress(manifest.TransferId, TransferState.Failed, targetHost, ex.Message);
            return false;
        }
        finally
        {
            Cleanup();
        }
    }

    private static async Task SendDirectoryMarkerAsync(Stream stream, int itemIndex, CancellationToken ct)
    {
        byte[] payload = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), itemIndex);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(4, 8), -1L); // -1 indicates directory marker
        await TransferProtocol.WriteFrameAsync(stream, FrameType.FileStart, payload, ct).ConfigureAwait(false);
    }

    private static async Task SendFileStartFrameAsync(Stream stream, int itemIndex, long resumeOffset, CancellationToken ct)
    {
        byte[] payload = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), itemIndex);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(4, 8), resumeOffset);
        await TransferProtocol.WriteFrameAsync(stream, FrameType.FileStart, payload, ct).ConfigureAwait(false);
    }

    private static async Task SendFileEndFrameAsync(Stream stream, int itemIndex, string sha256Hex, CancellationToken ct)
    {
        byte[] shaBytes = Encoding.UTF8.GetBytes(sha256Hex);
        byte[] payload = new byte[4 + shaBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), itemIndex);
        shaBytes.CopyTo(payload, 4);
        await TransferProtocol.WriteFrameAsync(stream, FrameType.FileEnd, payload, ct).ConfigureAwait(false);
    }

    private void ReportProgress(Guid transferId, TransferState state, string peerName, string? error = null)
    {
        var progress = _speedTracker.GetProgress(transferId, TransferDirection.Send, state, peerName, error);
        ProgressChanged?.Invoke(progress);
    }

    private void Cleanup()
    {
        try { _networkStream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _networkStream = null;
        _tcpClient = null;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Cleanup();
            _pauseLock.Dispose();
        }
    }
}
