using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.TransferEngine;

namespace FastEthernetTransfer.Core.GameUpdater;

public class GameUpdateClient : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;

    public async Task<(bool Accepted, string? FolderPath, string? Reason)> RequestRemoteFolderAsync(
        string targetHost,
        int targetPort,
        string requesterDeviceName,
        CancellationToken ct = default)
    {
        _tcpClient?.Dispose();
        _tcpClient = new TcpClient { NoDelay = true };
        await _tcpClient.ConnectAsync(targetHost, targetPort, ct).ConfigureAwait(false);
        _networkStream = _tcpClient.GetStream();

        var requestPayload = new GameUpdateFolderRequestPayload
        {
            RequestId = Guid.NewGuid().ToString(),
            RequesterDeviceName = requesterDeviceName
        };

        await TransferProtocol.WriteJsonFrameAsync(_networkStream, FrameType.GameUpdateFolderRequest, requestPayload, ct).ConfigureAwait(false);

        var (type, len) = await TransferProtocol.ReadHeaderAsync(_networkStream, ct).ConfigureAwait(false);
        if (type != FrameType.GameUpdateFolderResponse)
        {
            throw new InvalidOperationException($"Expected GameUpdateFolderResponse, got {type}");
        }

        var response = await TransferProtocol.ReadJsonPayloadAsync<GameUpdateFolderResponsePayload>(_networkStream, len, ct).ConfigureAwait(false);
        if (response == null)
        {
            return (false, null, "Null response from peer");
        }

        return (response.Accepted, response.SelectedFolderPath, response.Reason);
    }

    public async Task<FolderManifest> FetchRemoteManifestAsync(string folderPath, CancellationToken ct = default)
    {
        if (_networkStream == null || _tcpClient == null || !_tcpClient.Connected)
        {
            throw new InvalidOperationException("Not connected to peer. Request folder first.");
        }

        var requestPayload = new GameUpdateManifestRequestPayload
        {
            RequestId = Guid.NewGuid().ToString(),
            FolderPath = folderPath
        };

        await TransferProtocol.WriteJsonFrameAsync(_networkStream, FrameType.GameUpdateManifestRequest, requestPayload, ct).ConfigureAwait(false);

        var (type, len) = await TransferProtocol.ReadHeaderAsync(_networkStream, ct).ConfigureAwait(false);
        if (type != FrameType.GameUpdateManifestResponse)
        {
            throw new InvalidOperationException($"Expected GameUpdateManifestResponse, got {type}");
        }

        var response = await TransferProtocol.ReadJsonPayloadAsync<GameUpdateManifestResponsePayload>(_networkStream, len, ct).ConfigureAwait(false);
        if (response == null || !response.Success || response.Manifest == null)
        {
            throw new InvalidOperationException($"Failed to fetch remote manifest: {response?.Error ?? "Unknown error"}");
        }

        return response.Manifest;
    }

    public async Task<bool> RequestStartTransferAsync(
        string targetIp,
        int targetPort,
        string myIp,
        int myPort,
        string sourceFolderPath,
        string destinationFolderPath,
        System.Collections.Generic.List<string> relativePaths,
        CancellationToken ct = default)
    {
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(targetIp, targetPort, ct).ConfigureAwait(false);
        using var stream = client.GetStream();

        var payload = new GameUpdateStartTransferRequestPayload
        {
            RequestId = Guid.NewGuid().ToString(),
            TargetIp = myIp,
            TargetPort = myPort,
            SourceFolderPath = sourceFolderPath,
            DestinationFolderPath = destinationFolderPath,
            RelativePathsToTransfer = relativePaths
        };

        await TransferProtocol.WriteJsonFrameAsync(stream, FrameType.GameUpdateStartTransferRequest, payload, ct).ConfigureAwait(false);

        var (type, len) = await TransferProtocol.ReadHeaderAsync(stream, ct).ConfigureAwait(false);
        if (type != FrameType.GameUpdateStartTransferResponse)
        {
            throw new InvalidOperationException($"Expected GameUpdateStartTransferResponse, got {type}");
        }

        var response = await TransferProtocol.ReadJsonPayloadAsync<GameUpdateStartTransferResponsePayload>(stream, len, ct).ConfigureAwait(false);
        return response?.Accepted == true;
    }

    public void Dispose()
    {
        try { _networkStream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _networkStream = null;
        _tcpClient = null;
    }
}
