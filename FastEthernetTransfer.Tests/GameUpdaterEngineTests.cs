using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.GameUpdater;
using FastEthernetTransfer.Core.Models;
using Xunit;

namespace FastEthernetTransfer.Tests;

public class GameUpdaterEngineTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _destDir;

    public GameUpdaterEngineTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"FET_GameUpdaterTest_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "SourceGame");
        _destDir = Path.Combine(_testRoot, "DestGame");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
    }

    [Fact]
    public async Task Compare_IdenticalFiles_ClassifiedAsIdenticalAndSkipped()
    {
        // Arrange
        string fileContent = "Game asset data 123456789";
        string srcFile = Path.Combine(_sourceDir, "data.pak");
        string dstFile = Path.Combine(_destDir, "data.pak");
        File.WriteAllText(srcFile, fileContent);
        File.WriteAllText(dstFile, fileContent);

        // Act
        var session = new GameUpdateSession();
        await session.ScanSourceAsync(_sourceDir, computeHashes: true);
        await session.ScanDestinationAsync(_destDir, computeHashes: true);
        await session.CompareAsync();

        // Assert
        Assert.Single(session.ComparisonResults);
        var item = session.ComparisonResults.First();
        Assert.Equal(FileComparisonResult.Identical, item.Result);
        Assert.Empty(session.NewFiles);
        Assert.Empty(session.ChangedFiles);
        Assert.Single(session.IdenticalFiles);
        Assert.Equal(0, session.TotalTransferFiles);
        Assert.Equal(0, session.TotalTransferBytes);
    }

    [Fact]
    public async Task Compare_NewFiles_DetectedAndIncludedInTransfer()
    {
        // Arrange
        string srcFile = Path.Combine(_sourceDir, "new_dlc.pak");
        File.WriteAllBytes(srcFile, new byte[4096]);

        // Act
        var session = new GameUpdateSession();
        await session.ScanSourceAsync(_sourceDir, computeHashes: true);
        await session.ScanDestinationAsync(_destDir, computeHashes: true);
        await session.CompareAsync();

        // Assert
        Assert.Single(session.NewFiles);
        var item = session.NewFiles.First();
        Assert.Equal("new_dlc.pak", item.RelativePath);
        Assert.Equal(4096, item.SourceSize);
        Assert.Equal(1, session.TotalTransferFiles);
        Assert.Equal(4096, session.TotalTransferBytes);
    }

    [Fact]
    public async Task Compare_ChangedFiles_DetectedWhenSizeDiffers()
    {
        // Arrange
        string srcFile = Path.Combine(_sourceDir, "patch.exe");
        string dstFile = Path.Combine(_destDir, "patch.exe");
        File.WriteAllBytes(srcFile, new byte[8192]);
        File.WriteAllBytes(dstFile, new byte[4096]);

        // Act
        var session = new GameUpdateSession();
        await session.ScanSourceAsync(_sourceDir, computeHashes: true);
        await session.ScanDestinationAsync(_destDir, computeHashes: true);
        await session.CompareAsync();

        // Assert
        Assert.Single(session.ChangedFiles);
        var item = session.ChangedFiles.First();
        Assert.Equal("patch.exe", item.RelativePath);
        Assert.Equal(8192, item.SourceSize);
        Assert.Equal(4096, item.DestinationSize);
        Assert.Equal(1, session.TotalTransferFiles);
        Assert.Equal(8192, session.TotalTransferBytes);
    }

    [Fact]
    public async Task Compare_SameSizeDifferentContent_DetectedViaSha256Hash()
    {
        // Arrange: Exactly same size (100 bytes), but different byte contents
        byte[] srcBytes = new byte[100];
        byte[] dstBytes = new byte[100];
        Array.Fill(srcBytes, (byte)'A');
        Array.Fill(dstBytes, (byte)'B');

        string srcFile = Path.Combine(_sourceDir, "game_binary.dll");
        string dstFile = Path.Combine(_destDir, "game_binary.dll");
        File.WriteAllBytes(srcFile, srcBytes);
        File.WriteAllBytes(dstFile, dstBytes);

        // Act
        var session = new GameUpdateSession();
        await session.ScanSourceAsync(_sourceDir, computeHashes: true);
        await session.ScanDestinationAsync(_destDir, computeHashes: true);
        await session.CompareAsync();

        // Assert
        Assert.Single(session.ChangedFiles);
        var item = session.ChangedFiles.First();
        Assert.Equal(FileComparisonResult.Changed, item.Result);
        Assert.NotEqual(item.SourceHash, item.DestinationHash);
    }

    [Fact]
    public async Task Compare_TimestampDifferencesWithIdenticalContent_DoesNotClassifyAsChanged()
    {
        // Arrange: Content is identical, but modified timestamps are deliberately different
        byte[] content = new byte[1024];
        Random.Shared.NextBytes(content);

        string srcFile = Path.Combine(_sourceDir, "textures.bin");
        string dstFile = Path.Combine(_destDir, "textures.bin");
        File.WriteAllBytes(srcFile, content);
        File.WriteAllBytes(dstFile, content);

        File.SetLastWriteTimeUtc(srcFile, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(dstFile, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var session = new GameUpdateSession();
        await session.ScanSourceAsync(_sourceDir, computeHashes: true);
        await session.ScanDestinationAsync(_destDir, computeHashes: true);
        await session.CompareAsync();

        // Assert
        Assert.Single(session.IdenticalFiles);
        Assert.Empty(session.ChangedFiles);
    }

    [Fact]
    public async Task Compare_PreservesNestedDirectoriesAndUnicodeArabicNames()
    {
        // Arrange: Nested folders with Arabic and special Unicode names
        string subDirSrc = Path.Combine(_sourceDir, "Mods_تعديلات", "Level_مستوى_1");
        string subDirDst = Path.Combine(_destDir, "Mods_تعديلات", "Level_مستوى_1");
        Directory.CreateDirectory(subDirSrc);
        Directory.CreateDirectory(subDirDst);

        string srcFile = Path.Combine(subDirSrc, "ملف_تعريب_game_🌟.dat");
        File.WriteAllText(srcFile, "Arabic and unicode game localization");

        // Act
        var session = new GameUpdateSession();
        await session.ScanSourceAsync(_sourceDir, computeHashes: true);
        await session.ScanDestinationAsync(_destDir, computeHashes: true);
        await session.CompareAsync();

        // Assert
        Assert.Single(session.NewFiles);
        var item = session.NewFiles.First();
        Assert.Contains("Mods_تعديلات/Level_مستوى_1", item.RelativePath);
        Assert.Contains("ملف_تعريب", item.RelativePath);
    }

    [Fact]
    public async Task Compare_DestinationOnlyFiles_KeptByDefault()
    {
        // Arrange: User saves, configs, or mods present only on destination
        string saveFile = Path.Combine(_destDir, "savegame_slot1.sav");
        string modFile = Path.Combine(_destDir, "custom_mod.dll");
        File.WriteAllText(saveFile, "User save data");
        File.WriteAllText(modFile, "User mod DLL");

        // Act
        var session = new GameUpdateSession();
        await session.ScanSourceAsync(_sourceDir, computeHashes: true);
        await session.ScanDestinationAsync(_destDir, computeHashes: true);
        await session.CompareAsync();

        // Assert
        Assert.Equal(2, session.DestOnlyFiles.Count());
        Assert.Empty(session.NewFiles);
        Assert.Empty(session.ChangedFiles);
        // Ensure not marked for transfer
        Assert.Equal(0, session.TotalTransferFiles);
    }

    [Fact]
    public void SafeDelete_ConfinedToDestinationRoot_PreventsPathTraversal()
    {
        // Arrange: Create a file outside destination root
        string outsideDir = Path.Combine(_testRoot, "CriticalSystemFiles");
        Directory.CreateDirectory(outsideDir);
        string criticalFile = Path.Combine(outsideDir, "important.doc");
        File.WriteAllText(criticalFile, "Do not delete");

        string destFile = Path.Combine(_destDir, "obsolete_file.bak");
        File.WriteAllText(destFile, "Safe to delete");

        var candidates = new[]
        {
            new ComparedFile
            {
                RelativePath = "obsolete_file.bak",
                Result = FileComparisonResult.DestinationOnly
            },
            new ComparedFile
            {
                RelativePath = "../CriticalSystemFiles/important.doc", // Path traversal attempt!
                Result = FileComparisonResult.DestinationOnly
            },
            new ComparedFile
            {
                RelativePath = "C:\\Windows\\System32\\notepad.exe", // Rooted absolute path attempt!
                Result = FileComparisonResult.DestinationOnly
            }
        };

        // Act
        var deleted = GameUpdateSession.SafeDeleteDestinationOnlyFiles(_destDir, candidates);

        // Assert
        Assert.Single(deleted);
        Assert.Equal("obsolete_file.bak", deleted[0]);
        Assert.False(File.Exists(destFile));
        Assert.True(File.Exists(criticalFile)); // Outside file was protected!
    }

    [Fact]
    public void BuildTransferManifest_CreatesValidDirectoryAndFileItems()
    {
        // Arrange
        var session = new GameUpdateSession
        {
            ComparisonResults =
            [
                new ComparedFile
                {
                    RelativePath = "Engine/Binaries/Win64/game.exe",
                    SourceSize = 1048576,
                    SourceHash = "abcdef123456",
                    Result = FileComparisonResult.New
                },
                new ComparedFile
                {
                    RelativePath = "Content/Paks/chunk0.pak",
                    SourceSize = 5242880,
                    SourceHash = "7890abcdef",
                    Result = FileComparisonResult.Changed
                },
                new ComparedFile
                {
                    RelativePath = "Content/Paks/unchanged.pak",
                    SourceSize = 9999,
                    Result = FileComparisonResult.Identical
                }
            ]
        };

        // Act
        var manifest = session.BuildTransferManifest(Guid.NewGuid(), "SOURCE-PC");

        // Assert
        Assert.Equal(2, manifest.TotalFiles);
        Assert.Equal(1048576 + 5242880, manifest.TotalBytes);
        Assert.True(manifest.TotalFolders >= 3); // Engine, Engine/Binaries, Engine/Binaries/Win64, Content, Content/Paks
        Assert.Contains(manifest.Items, i => i.IsDirectory && i.RelativePath == "Engine/Binaries/Win64");
        Assert.Contains(manifest.Items, i => !i.IsDirectory && i.RelativePath == "Engine/Binaries/Win64/game.exe" && i.Size == 1048576);
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
