using System;
using System.IO;
using System.Linq;
using FastEthernetTransfer.Core.TransferEngine;
using Xunit;

namespace FastEthernetTransfer.Tests;

public class ManifestBuilderTests : IDisposable
{
    private readonly string _testRoot;

    public ManifestBuilderTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"FET_ManifestTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public void ManifestBuilder_UnicodeAndArabicFiles_PreservedProperly()
    {
        // Setup complex directory tree
        string folderA = Path.Combine(_testRoot, "Folder_اختبار");
        string emptySubFolder = Path.Combine(folderA, "Empty_مجلد_فارغ");
        string deepSubFolder = Path.Combine(folderA, "Sub1", "Sub2");
        Directory.CreateDirectory(emptySubFolder);
        Directory.CreateDirectory(deepSubFolder);

        // Unicode and Arabic file names
        string file1 = Path.Combine(folderA, "ملف_عربي_كبير.bin");
        string file2 = Path.Combine(deepSubFolder, "café_spécial_🌟.dat");
        string file3 = Path.Combine(folderA, "spaced file name with symbols #@!.txt");

        File.WriteAllBytes(file1, new byte[1024]);
        File.WriteAllBytes(file2, new byte[2048]);
        File.WriteAllText(file3, "Plain text test content");

        var manifest = ManifestBuilder.BuildFromPaths(
            filePaths: [],
            folderPaths: [folderA],
            senderId: Guid.NewGuid(),
            senderDeviceName: "TEST-PC");

        Assert.Equal(3, manifest.TotalFiles);
        Assert.True(manifest.TotalFolders >= 4); // Root + empty folder + sub1 + sub2
        Assert.Equal(1024 + 2048 + new FileInfo(file3).Length, manifest.TotalBytes);

        // Verify empty folder marker exists in items
        var emptyFolderItem = manifest.Items.FirstOrDefault(i => i.IsDirectory && i.RelativePath.Contains("مجلد_فارغ"));
        Assert.NotNull(emptyFolderItem);

        // Verify Arabic and unicode file relative paths
        var arabicFileItem = manifest.Items.FirstOrDefault(i => !i.IsDirectory && i.RelativePath.Contains("ملف_عربي"));
        Assert.NotNull(arabicFileItem);
        Assert.Equal(1024, arabicFileItem.Size);

        var unicodeFileItem = manifest.Items.FirstOrDefault(i => !i.IsDirectory && i.RelativePath.Contains("café_spécial"));
        Assert.NotNull(unicodeFileItem);
        Assert.Equal(2048, unicodeFileItem.Size);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch { }
    }
}
