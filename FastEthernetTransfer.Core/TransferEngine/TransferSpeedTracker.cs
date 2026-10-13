using System;
using System.Collections.Generic;
using System.Diagnostics;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.TransferEngine;

public class TransferSpeedTracker
{
    private readonly Stopwatch _stopwatch = new();
    private readonly Lock _lock = new();

    private long _totalBytes;
    private long _totalBytesTransferred;
    private long _currentFileBytesTransferred;
    private long _currentFileSize;

    private int _totalFiles;
    private int _completedFiles;
    private int _currentFileIndex;
    private string _currentFileName = string.Empty;

    private double _peakSpeed;
    private readonly Queue<(long Bytes, long TimestampTicks)> _samples = new();
    private const int WindowDurationMs = 1000;

    public void Start(long totalBytes, int totalFiles)
    {
        lock (_lock)
        {
            _totalBytes = totalBytes;
            _totalFiles = totalFiles;
            _totalBytesTransferred = 0;
            _completedFiles = 0;
            _peakSpeed = 0;
            _samples.Clear();
            _stopwatch.Restart();
            _samples.Enqueue((0, _stopwatch.ElapsedTicks));
        }
    }

    public void UpdateCurrentFile(int fileIndex, string fileName, long fileSize, long alreadyTransferred = 0)
    {
        lock (_lock)
        {
            _currentFileIndex = fileIndex;
            _currentFileName = fileName;
            _currentFileSize = fileSize;
            _currentFileBytesTransferred = alreadyTransferred;
        }
    }

    public void AddBytesTransferred(long bytes)
    {
        if (bytes <= 0) return;

        lock (_lock)
        {
            _totalBytesTransferred += bytes;
            _currentFileBytesTransferred += bytes;

            long currentTicks = _stopwatch.ElapsedTicks;
            _samples.Enqueue((_totalBytesTransferred, currentTicks));

            // Evict samples older than WindowDurationMs
            long windowTicks = (long)(WindowDurationMs * (Stopwatch.Frequency / 1000.0));
            while (_samples.Count > 2 && (currentTicks - _samples.Peek().TimestampTicks) > windowTicks)
            {
                _samples.Dequeue();
            }

            // Calculate instantaneous speed
            if (_samples.Count >= 2)
            {
                var oldest = _samples.Peek();
                long deltaBytes = _totalBytesTransferred - oldest.Bytes;
                long deltaTicks = currentTicks - oldest.TimestampTicks;

                if (deltaTicks > 0)
                {
                    double currentSpeed = deltaBytes / ((double)deltaTicks / Stopwatch.Frequency);
                    if (currentSpeed > _peakSpeed)
                    {
                        _peakSpeed = currentSpeed;
                    }
                }
            }
        }
    }

    public void FileCompleted()
    {
        lock (_lock)
        {
            _completedFiles++;
        }
    }

    public TransferProgress GetProgress(Guid transferId, TransferDirection direction, TransferState state, string peerName, string? errorMessage = null)
    {
        lock (_lock)
        {
            double currentSpeed = 0;
            long currentTicks = _stopwatch.ElapsedTicks;

            if (_samples.Count >= 2 && state == TransferState.Transferring)
            {
                var oldest = _samples.Peek();
                long deltaBytes = _totalBytesTransferred - oldest.Bytes;
                long deltaTicks = currentTicks - oldest.TimestampTicks;

                if (deltaTicks > 0)
                {
                    currentSpeed = deltaBytes / ((double)deltaTicks / Stopwatch.Frequency);
                }
            }

            double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            double avgSpeed = (elapsedSeconds > 0.1 && _totalBytesTransferred > 0)
                ? _totalBytesTransferred / elapsedSeconds
                : currentSpeed;

            long remainingBytes = Math.Max(0L, _totalBytes - _totalBytesTransferred);
            TimeSpan eta = TimeSpan.Zero;

            // Compute ETA based on smoothed blend of current and average speed
            double effectiveSpeed = (currentSpeed > 0) ? (currentSpeed * 0.7 + avgSpeed * 0.3) : avgSpeed;
            if (effectiveSpeed > 1024 && remainingBytes > 0)
            {
                double secondsRemaining = remainingBytes / effectiveSpeed;
                if (secondsRemaining < 86400 * 7) // Cap at 7 days
                {
                    eta = TimeSpan.FromSeconds(secondsRemaining);
                }
            }

            return new TransferProgress
            {
                TransferId = transferId,
                Direction = direction,
                State = state,
                PeerName = peerName,
                CurrentFileName = _currentFileName,
                CurrentFileIndex = _currentFileIndex,
                TotalFiles = _totalFiles,
                CompletedFiles = _completedFiles,
                CurrentFileBytesTransferred = _currentFileBytesTransferred,
                CurrentFileSize = _currentFileSize,
                TotalBytesTransferred = _totalBytesTransferred,
                TotalBytes = _totalBytes,
                CurrentSpeedBytesPerSec = currentSpeed,
                AverageSpeedBytesPerSec = avgSpeed,
                PeakSpeedBytesPerSec = _peakSpeed,
                EstimatedTimeRemaining = eta,
                ErrorMessage = errorMessage
            };
        }
    }
}
