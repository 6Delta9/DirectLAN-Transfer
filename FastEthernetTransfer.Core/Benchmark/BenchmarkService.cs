using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Network;
using FastEthernetTransfer.Core.TransferEngine;

namespace FastEthernetTransfer.Core.Benchmark;

public class BenchmarkService
{
    private readonly NetworkInterfaceDetector _nicDetector;

    public BenchmarkService(NetworkInterfaceDetector nicDetector)
    {
        _nicDetector = nicDetector;
    }

    public async Task<BenchmarkResult> RunBenchmarkAsync(string testDirectory, string? targetPeerIp = null, int targetPeerPort = 45779, Action<string>? statusCallback = null, CancellationToken ct = default)
    {
        var result = new BenchmarkResult();
        var bestNic = _nicDetector.GetBestEthernetAdapter();
        result.AdapterName = bestNic?.Name ?? "Ethernet";
        result.LinkSpeedBitsPerSec = bestNic?.SpeedBitsPerSec ?? 0;
        result.LinkSpeedFormatted = bestNic?.SpeedFormatted ?? "Unknown";

        const int testSizeBytes = 512 * 1024 * 1024; // 512 MB
        const int bufferSize = 2 * 1024 * 1024; // 2 MB chunks
        string testFilePath = Path.Combine(testDirectory, $"benchmark_test_{Guid.NewGuid():N}.tmp");

        try
        {
            // 1. Disk Write Benchmark
            statusCallback?.Invoke("Benchmarking Disk Sequential Write (512 MB)...");
            result.DiskWriteSpeedMBs = await BenchmarkDiskWriteAsync(testFilePath, testSizeBytes, bufferSize, ct).ConfigureAwait(false);
            Logger.Info($"Benchmark Disk Write: {result.DiskWriteSpeedMBs:F1} MB/s");

            // 2. Disk Read Benchmark
            statusCallback?.Invoke("Benchmarking Disk Sequential Read (512 MB)...");
            result.DiskReadSpeedMBs = await BenchmarkDiskReadAsync(testFilePath, testSizeBytes, bufferSize, ct).ConfigureAwait(false);
            Logger.Info($"Benchmark Disk Read: {result.DiskReadSpeedMBs:F1} MB/s");

            // 3. Network Throughput Benchmark
            statusCallback?.Invoke("Benchmarking Network Socket Throughput...");
            if (!string.IsNullOrEmpty(targetPeerIp))
            {
                result.NetworkThroughputMBs = await BenchmarkRemoteNetworkAsync(targetPeerIp, targetPeerPort, testSizeBytes, bufferSize, ct).ConfigureAwait(false);
            }
            else
            {
                result.NetworkThroughputMBs = await BenchmarkLoopbackNetworkAsync(testSizeBytes, bufferSize, ct).ConfigureAwait(false);
            }
            Logger.Info($"Benchmark Network: {result.NetworkThroughputMBs:F1} MB/s");

            // 4. Calculate End-to-End estimated throughput
            // Bottleneck is minimum of Read, Network, Write
            double bottleneck = Math.Min(result.DiskReadSpeedMBs, Math.Min(result.DiskWriteSpeedMBs, result.NetworkThroughputMBs));
            // Realistic pipeline factor (~92-96% of bottleneck due to framing & TCP overhead)
            result.EndToEndSpeedMBs = Math.Round(bottleneck * 0.94, 1);

            result.SummaryText = $"Adapter: {result.LinkSpeedFormatted} | Disk R/W: {result.DiskReadSpeedMBs:F0}/{result.DiskWriteSpeedMBs:F0} MB/s | Net: {result.NetworkThroughputMBs:F0} MB/s";
            statusCallback?.Invoke("Benchmark completed!");
        }
        catch (Exception ex)
        {
            Logger.Error("Benchmark failed", ex);
            statusCallback?.Invoke($"Benchmark error: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(testFilePath))
                {
                    File.Delete(testFilePath);
                }
            }
            catch { }
        }

        return result;
    }

    private static async Task<double> BenchmarkDiskWriteAsync(string path, long totalBytes, int bufferSize, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        // Fill with dummy data
        Random.Shared.NextBytes(buffer.AsSpan(0, Math.Min(1024, bufferSize)));

        try
        {
            var sw = Stopwatch.StartNew();
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = bufferSize
            };

            using (var fs = new FileStream(path, options))
            {
                long written = 0;
                while (written < totalBytes)
                {
                    int toWrite = (int)Math.Min(bufferSize, totalBytes - written);
                    await fs.WriteAsync(buffer.AsMemory(0, toWrite), ct).ConfigureAwait(false);
                    written += toWrite;
                }
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            sw.Stop();
            double seconds = sw.Elapsed.TotalSeconds;
            return seconds > 0 ? (totalBytes / (1024.0 * 1024.0)) / seconds : 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<double> BenchmarkDiskReadAsync(string path, long totalBytes, int bufferSize, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            var sw = Stopwatch.StartNew();
            var options = new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = bufferSize
            };

            using (var fs = new FileStream(path, options))
            {
                long readTotal = 0;
                int read;
                while ((read = await fs.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    readTotal += read;
                }
            }

            sw.Stop();
            double seconds = sw.Elapsed.TotalSeconds;
            return seconds > 0 ? (totalBytes / (1024.0 * 1024.0)) / seconds : 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<double> BenchmarkLoopbackNetworkAsync(long totalBytes, int bufferSize, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            serverClient.NoDelay = true;
            serverClient.ReceiveBufferSize = bufferSize;
            using var serverStream = serverClient.GetStream();

            byte[] recvBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                long received = 0;
                while (received < totalBytes)
                {
                    int read = await serverStream.ReadAsync(recvBuffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false);
                    if (read == 0) break;
                    received += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(recvBuffer);
            }
        }, ct);

        using var client = new TcpClient();
        client.NoDelay = true;
        client.SendBufferSize = bufferSize;
        await client.ConnectAsync(IPAddress.Loopback, port, ct).ConfigureAwait(false);
        using var clientStream = client.GetStream();

        byte[] sendBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var sw = Stopwatch.StartNew();

        try
        {
            long sent = 0;
            while (sent < totalBytes)
            {
                int toSend = (int)Math.Min(bufferSize, totalBytes - sent);
                await clientStream.WriteAsync(sendBuffer.AsMemory(0, toSend), ct).ConfigureAwait(false);
                sent += toSend;
            }
            await clientStream.FlushAsync(ct).ConfigureAwait(false);
            await serverTask.ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sendBuffer);
            listener.Stop();
        }

        sw.Stop();
        double seconds = sw.Elapsed.TotalSeconds;
        return seconds > 0 ? (totalBytes / (1024.0 * 1024.0)) / seconds : 0;
    }

    private static async Task<double> BenchmarkRemoteNetworkAsync(string ip, int port, long totalBytes, int bufferSize, CancellationToken ct)
    {
        using var client = new TcpClient();
        client.NoDelay = true;
        client.SendBufferSize = bufferSize;
        client.ReceiveBufferSize = bufferSize;

        await client.ConnectAsync(ip, port, ct).ConfigureAwait(false);
        using var stream = client.GetStream();

        // Send BenchmarkStart frame with totalBytes
        byte[] startPayload = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(startPayload.AsSpan(0, 8), totalBytes);
        await TransferProtocol.WriteFrameAsync(stream, FrameType.BenchmarkStart, startPayload, ct).ConfigureAwait(false);

        byte[] sendBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var sw = Stopwatch.StartNew();

        try
        {
            long sent = 0;
            while (sent < totalBytes)
            {
                int toSend = (int)Math.Min(bufferSize, totalBytes - sent);
                await TransferProtocol.WriteFrameAsync(stream, FrameType.BenchmarkData, sendBuffer.AsMemory(0, toSend), ct).ConfigureAwait(false);
                sent += toSend;
            }

            // Await BenchmarkEnd from peer
            var (ackType, _) = await TransferProtocol.ReadHeaderAsync(stream, ct).ConfigureAwait(false);
            sw.Stop();

            double seconds = sw.Elapsed.TotalSeconds;
            return seconds > 0 ? (totalBytes / (1024.0 * 1024.0)) / seconds : 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sendBuffer);
        }
    }
}
