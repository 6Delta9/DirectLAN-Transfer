using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FastEthernetTransfer.Core.Benchmark;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Network;

namespace FastEthernetTransfer.Wpf.ViewModels;

public class BenchmarkViewModel : ViewModelBase
{
    private readonly BenchmarkService _benchmarkService;
    private readonly NetworkInterfaceDetector _nicDetector;

    private bool _isRunning;
    private string _statusMessage = "Ready to run Ethernet and Disk benchmark.";
    private BenchmarkResult? _result;

    private string _adapterName = "Ethernet";
    private string _linkSpeedFormatted = "2.5 Gbps";
    private double _diskReadMBs;
    private double _diskWriteMBs;
    private double _networkMBs;
    private double _endToEndMBs;

    public ICommand StartBenchmarkCommand { get; }

    public BenchmarkViewModel(NetworkInterfaceDetector nicDetector)
    {
        _nicDetector = nicDetector;
        _benchmarkService = new BenchmarkService(nicDetector);
        StartBenchmarkCommand = new AsyncRelayCommand(OnStartBenchmarkAsync, () => !IsRunning);

        var nic = _nicDetector.GetBestEthernetAdapter();
        if (nic != null)
        {
            AdapterName = nic.Name;
            LinkSpeedFormatted = nic.SpeedFormatted;
        }
    }

    public bool IsRunning { get => _isRunning; set { if (SetProperty(ref _isRunning, value)) OnPropertyChanged(nameof(CanStart)); } }
    public bool CanStart => !IsRunning;
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public BenchmarkResult? Result { get => _result; set => SetProperty(ref _result, value); }

    public string AdapterName { get => _adapterName; set => SetProperty(ref _adapterName, value); }
    public string LinkSpeedFormatted { get => _linkSpeedFormatted; set => SetProperty(ref _linkSpeedFormatted, value); }
    public double DiskReadMBs { get => _diskReadMBs; set => SetProperty(ref _diskReadMBs, value); }
    public double DiskWriteMBs { get => _diskWriteMBs; set => SetProperty(ref _diskWriteMBs, value); }
    public double NetworkMBs { get => _networkMBs; set => SetProperty(ref _networkMBs, value); }
    public double EndToEndMBs { get => _endToEndMBs; set => SetProperty(ref _endToEndMBs, value); }

    private async Task OnStartBenchmarkAsync()
    {
        IsRunning = true;
        StatusMessage = "Initializing benchmark...";

        try
        {
            string tempDir = Path.GetTempPath();
            var res = await _benchmarkService.RunBenchmarkAsync(
                tempDir,
                targetPeerIp: null, // Loopback network test
                statusCallback: msg => StatusMessage = msg,
                ct: CancellationToken.None);

            Result = res;
            AdapterName = res.AdapterName;
            LinkSpeedFormatted = res.LinkSpeedFormatted;
            DiskReadMBs = res.DiskReadSpeedMBs;
            DiskWriteMBs = res.DiskWriteSpeedMBs;
            NetworkMBs = res.NetworkThroughputMBs;
            EndToEndMBs = res.EndToEndSpeedMBs;
            StatusMessage = "Benchmark completed successfully!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Benchmark error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
