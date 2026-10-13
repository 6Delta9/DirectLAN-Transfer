using System;

namespace FastEthernetTransfer.Core.Models;

public class BenchmarkResult
{
    public string AdapterName { get; set; } = string.Empty;
    public long LinkSpeedBitsPerSec { get; set; }
    public string LinkSpeedFormatted { get; set; } = string.Empty;

    public double DiskReadSpeedMBs { get; set; }
    public double DiskWriteSpeedMBs { get; set; }
    public double NetworkThroughputMBs { get; set; }
    public double EndToEndSpeedMBs { get; set; }

    public DateTime CompletedAt { get; set; } = DateTime.Now;
    public string SummaryText { get; set; } = string.Empty;
}
