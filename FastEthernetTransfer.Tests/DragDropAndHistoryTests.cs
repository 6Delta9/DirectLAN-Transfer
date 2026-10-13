using System;
using System.IO;
using System.Linq;
using FastEthernetTransfer.Core.Models;
using FastEthernetTransfer.Core.Storage;
using FastEthernetTransfer.Core.TransferEngine;
using Xunit;

namespace FastEthernetTransfer.Tests;

public class DragDropAndHistoryTests : IDisposable
{
    private readonly string _testDir;

    public DragDropAndHistoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "FET_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
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

    [Fact]
    public void DragDropProcessor_ValidatesFilesAndFoldersCorrectly()
    {
        string file1 = Path.Combine(_testDir, "test1.bin");
        File.WriteAllBytes(file1, new byte[1024]); // 1 KB

        string subDir = Path.Combine(_testDir, "SubFolder");
        Directory.CreateDirectory(subDir);
        string file2 = Path.Combine(subDir, "test2.bin");
        File.WriteAllBytes(file2, new byte[2048]); // 2 KB

        var results = DragDropItemProcessor.ProcessDroppedPaths([file1, subDir]);

        Assert.Equal(2, results.Count);

        var fileItem = results.FirstOrDefault(r => !r.IsDirectory);
        Assert.NotNull(fileItem);
        Assert.Equal("test1.bin", fileItem.Name);
        Assert.Equal(1024, fileItem.SizeBytes);

        var dirItem = results.FirstOrDefault(r => r.IsDirectory);
        Assert.NotNull(dirItem);
        Assert.Equal("SubFolder", dirItem.Name);
        Assert.Equal(2048, dirItem.SizeBytes);
    }

    [Fact]
    public void DragDropProcessor_IgnoresDuplicateAndInvalidPaths()
    {
        string file1 = Path.Combine(_testDir, "sample.txt");
        File.WriteAllText(file1, "Hello World");

        var results = DragDropItemProcessor.ProcessDroppedPaths([
            file1,
            file1, // duplicate
            Path.Combine(_testDir, "non_existent_file.xyz"),
            "",
            "   "
        ]);

        Assert.Single(results);
        Assert.Equal("sample.txt", results[0].Name);
    }

    [Fact]
    public void DragDropProcessor_HandlesEmptyOrNullInputGracefully()
    {
        var nullResult = DragDropItemProcessor.ProcessDroppedPaths(null);
        Assert.Empty(nullResult);

        var emptyResult = DragDropItemProcessor.ProcessDroppedPaths([]);
        Assert.Empty(emptyResult);
    }

    [Fact]
    public void HistoryService_AddsAndLoadsItemsSuccessfully()
    {
        var historyService = new HistoryService();
        historyService.ClearHistory();

        var item1 = new TransferHistoryItem
        {
            TransferId = Guid.NewGuid(),
            Direction = TransferDirection.Send,
            PeerName = "PC-ALPHA",
            TotalBytes = 1048576,
            TotalFiles = 5,
            AverageSpeedBytesPerSec = 112.5 * 1024 * 1024,
            State = TransferState.Completed,
            TimestampUtc = DateTime.UtcNow
        };

        historyService.AddItem(item1);

        var loaded = historyService.LoadHistory();
        Assert.NotEmpty(loaded);
        Assert.Equal(TransferDirection.Send, loaded[0].Direction);
        Assert.Equal("PC-ALPHA", loaded[0].PeerName);
        Assert.Equal(1048576, loaded[0].TotalBytes);
        Assert.Equal(TransferState.Completed, loaded[0].State);
        Assert.Equal("Completed", loaded[0].StateDisplay);
        Assert.Equal("Sent", loaded[0].DirectionDisplay);

        historyService.ClearHistory();
        var afterClear = historyService.LoadHistory();
        Assert.Empty(afterClear);
    }
}
