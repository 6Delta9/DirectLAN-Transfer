using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Benchmark;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Network;
using Xunit;

namespace FastEthernetTransfer.Tests;

public class BenchmarkAndConflictTests : IDisposable
{
    private readonly string _testDir;

    public BenchmarkAndConflictTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"FET_BenchTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task BenchmarkService_ExecutesDiskAndNetworkTests_ReturnsSpeeds()
    {
        var nicDetector = new NetworkInterfaceDetector();
        var benchmark = new BenchmarkService(nicDetector);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await benchmark.RunBenchmarkAsync(
            testDirectory: _testDir,
            targetPeerIp: null, // Test loopback network
            ct: cts.Token);

        Assert.NotNull(result);
        Assert.True(result.DiskWriteSpeedMBs > 0, "Disk write speed should be greater than zero");
        Assert.True(result.DiskReadSpeedMBs > 0, "Disk read speed should be greater than zero");
        Assert.True(result.NetworkThroughputMBs > 0, "Network throughput should be greater than zero");
        Assert.True(result.EndToEndSpeedMBs > 0, "End-to-end estimated speed should be greater than zero");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }
}
