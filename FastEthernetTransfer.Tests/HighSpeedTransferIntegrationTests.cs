using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Storage;
using FastEthernetTransfer.Core.TransferEngine;
using Xunit;

namespace FastEthernetTransfer.Tests;

public class HighSpeedTransferIntegrationTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly string _destDir;
    private readonly SettingsService _settingsService;

    public HighSpeedTransferIntegrationTests()
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), $"FET_Test_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(baseTemp, "Source");
        _destDir = Path.Combine(baseTemp, "Destination");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);

        _settingsService = new SettingsService();
        _settingsService.Settings.TcpPort = 49152 + Random.Shared.Next(0, 1000); // Dynamic ephemeral port
        _settingsService.Settings.DefaultDownloadPath = _destDir;
        _settingsService.Settings.AutoAcceptTrustedPeers = true;
        _settingsService.Settings.BufferSizeBytes = 512 * 1024;
    }

    [Fact]
    public async Task Transfer_EndToEnd_VerifiesSha256AndFileContent()
    {
        // 1. Create source test files
        string sourceFile1 = Path.Combine(_sourceDir, "DataFile1.bin");
        string sourceFile2 = Path.Combine(_sourceDir, "ملف_عربي.dat");

        byte[] data1 = new byte[2 * 1024 * 1024]; // 2 MB
        byte[] data2 = new byte[1024 * 1024];     // 1 MB
        Random.Shared.NextBytes(data1);
        Random.Shared.NextBytes(data2);

        await File.WriteAllBytesAsync(sourceFile1, data1);
        await File.WriteAllBytesAsync(sourceFile2, data2);

        string expectedHash1 = ComputeSha256(sourceFile1);
        string expectedHash2 = ComputeSha256(sourceFile2);

        // 2. Start Receiver with dynamic port (0)
        _settingsService.Settings.TcpPort = 0;
        using var receiver = new HighSpeedTcpReceiver(_settingsService);
        bool started = receiver.Start();
        Assert.True(started);
        int port = receiver.BoundPort;

        var requestAcceptedTcs = new TaskCompletionSource<bool>();
        receiver.TransferRequested += req =>
        {
            req.DecisionSource.SetResult((true, _destDir, ConflictResolution.Replace));
            requestAcceptedTcs.TrySetResult(true);
        };

        // 3. Build Manifest
        var manifest = ManifestBuilder.BuildFromPaths(
            filePaths: [sourceFile1, sourceFile2],
            folderPaths: [],
            senderId: Guid.NewGuid(),
            senderDeviceName: "SenderPC");

        var pathMap = new Dictionary<string, string>
        {
            [Path.GetFileName(sourceFile1)] = sourceFile1,
            [Path.GetFileName(sourceFile2)] = sourceFile2
        };

        // 4. Send Transfer
        using var sender = new HighSpeedTcpSender(_settingsService.Settings);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        bool success = await sender.SendTransferAsync(
            "127.0.0.1",
            port,
            manifest,
            pathMap,
            cts.Token);

        Assert.True(success);

        // 5. Verify received files and hashes
        string destFile1 = Path.Combine(_destDir, Path.GetFileName(sourceFile1));
        string destFile2 = Path.Combine(_destDir, Path.GetFileName(sourceFile2));

        Assert.True(File.Exists(destFile1), $"Destination file {destFile1} was not created.");
        Assert.True(File.Exists(destFile2), $"Destination file {destFile2} was not created.");

        string actualHash1 = ComputeSha256(destFile1);
        string actualHash2 = ComputeSha256(destFile2);

        Assert.Equal(expectedHash1, actualHash1);
        Assert.Equal(expectedHash2, actualHash2);
    }

    [Fact]
    public async Task Transfer_ResumeInterruptedFile_ResumesCorrectly()
    {
        // 1. Create a 4 MB source file
        string sourceFile = Path.Combine(_sourceDir, "LargeFileToResume.iso");
        byte[] fileBytes = new byte[4 * 1024 * 1024]; // 4 MB
        Random.Shared.NextBytes(fileBytes);
        await File.WriteAllBytesAsync(sourceFile, fileBytes);
        string expectedSha = ComputeSha256(sourceFile);

        // 2. Simulate partial destination file (first 1.5 MB already transferred)
        string destFinalFile = Path.Combine(_destDir, Path.GetFileName(sourceFile));
        string destPartFile = destFinalFile + ".fetpart";
        int partialLength = 1536 * 1024; // 1.5 MB
        byte[] partialBytes = new byte[partialLength];
        Array.Copy(fileBytes, 0, partialBytes, 0, partialLength);
        await File.WriteAllBytesAsync(destPartFile, partialBytes);

        // 3. Start receiver with port configured
        _settingsService.Settings.TcpPort = 0;
        using var receiver = new HighSpeedTcpReceiver(_settingsService);
        bool started = receiver.Start();
        Assert.True(started);
        int port = receiver.BoundPort;

        receiver.TransferRequested += req =>
        {
            req.DecisionSource.SetResult((true, _destDir, ConflictResolution.Ask));
        };

        // 4. Build manifest
        var manifest = ManifestBuilder.BuildFromPaths(
            filePaths: [sourceFile],
            folderPaths: [],
            senderId: Guid.NewGuid(),
            senderDeviceName: "SenderPC");

        var pathMap = new Dictionary<string, string>
        {
            [Path.GetFileName(sourceFile)] = sourceFile
        };

        // 5. Send with resume support
        using var sender = new HighSpeedTcpSender(_settingsService.Settings);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        bool success = await sender.SendTransferAsync(
            "127.0.0.1",
            port,
            manifest,
            pathMap,
            cts.Token);

        Assert.True(success);

        // 6. Verify final file completed and matches full source SHA-256
        Assert.True(File.Exists(destFinalFile));
        Assert.False(File.Exists(destPartFile)); // Part file committed
        Assert.Equal(fileBytes.Length, new FileInfo(destFinalFile).Length);

        string actualSha = ComputeSha256(destFinalFile);
        Assert.Equal(expectedSha, actualSha);
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        byte[] hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        try
        {
            string parent = Path.GetDirectoryName(_sourceDir) ?? "";
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }
        catch { }
    }
}
