using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.GameUpdater;

public class GameUpdateSession
{
    public FolderManifest? SourceManifest { get; set; }
    public FolderManifest? DestinationManifest { get; set; }
    public List<ComparedFile> ComparisonResults { get; set; } = [];
    public TransferManifest? TransferManifest { get; private set; }

    public IEnumerable<ComparedFile> NewFiles => ComparisonResults.Where(x => x.Result == FileComparisonResult.New);
    public IEnumerable<ComparedFile> ChangedFiles => ComparisonResults.Where(x => x.Result == FileComparisonResult.Changed);
    public IEnumerable<ComparedFile> IdenticalFiles => ComparisonResults.Where(x => x.Result == FileComparisonResult.Identical);
    public IEnumerable<ComparedFile> DestOnlyFiles => ComparisonResults.Where(x => x.Result == FileComparisonResult.DestinationOnly);
    public IEnumerable<ComparedFile> UnreadableFiles => ComparisonResults.Where(x => x.Result == FileComparisonResult.Unreadable);

    public long TotalTransferBytes => TransferManifest?.TotalBytes ?? ComparisonResults
        .Where(r => r.Result == FileComparisonResult.New || r.Result == FileComparisonResult.Changed)
        .Sum(r => r.SourceSize);

    public int TotalTransferFiles => TransferManifest?.TotalFiles ?? ComparisonResults
        .Count(r => r.Result == FileComparisonResult.New || r.Result == FileComparisonResult.Changed);

    public async Task ScanSourceAsync(string rootPath, IProgress<(int scanned, string currentFile)>? progress = null, CancellationToken ct = default, bool computeHashes = true)
    {
        SourceManifest = await FolderManifest.ScanFolderAsync(rootPath, progress, ct, computeHashes).ConfigureAwait(false);
    }

    public async Task ScanDestinationAsync(string rootPath, IProgress<(int scanned, string currentFile)>? progress = null, CancellationToken ct = default, bool computeHashes = false)
    {
        DestinationManifest = await FolderManifest.ScanFolderAsync(rootPath, progress, ct, computeHashes).ConfigureAwait(false);
    }

    public async Task CompareAsync(IProgress<(int compared, int total, string currentFile)>? progress = null, CancellationToken ct = default)
    {
        if (SourceManifest == null || DestinationManifest == null)
            throw new InvalidOperationException("Source and Destination manifests must be set before comparison.");

        ComparisonResults = await FolderComparer.CompareAsync(SourceManifest, DestinationManifest, progress, ct).ConfigureAwait(false);
    }

    public TransferManifest BuildTransferManifest(Guid senderId, string senderDeviceName)
    {
        var filesToTransfer = ComparisonResults
            .Where(r => r.Result == FileComparisonResult.New || r.Result == FileComparisonResult.Changed)
            .ToList();

        var manifest = new TransferManifest
        {
            TransferId = Guid.NewGuid(),
            SenderId = senderId,
            SenderDeviceName = senderDeviceName,
            TotalBytes = filesToTransfer.Sum(f => f.SourceSize),
            TotalFiles = filesToTransfer.Count
        };

        // Create directory structure markers first
        var directorySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in filesToTransfer)
        {
            string? dir = Path.GetDirectoryName(file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            while (!string.IsNullOrEmpty(dir))
            {
                string normalizedDir = dir.Replace('\\', '/');
                directorySet.Add(normalizedDir);
                dir = Path.GetDirectoryName(dir);
            }
        }

        int index = 0;
        foreach (var dir in directorySet.OrderBy(d => d.Length))
        {
            manifest.Items.Add(new TransferItem
            {
                ItemIndex = index++,
                RelativePath = dir,
                Size = 0,
                IsDirectory = true,
                LastWriteTimeUtc = DateTime.UtcNow
            });
        }
        manifest.TotalFolders = directorySet.Count;

        foreach (var file in filesToTransfer)
        {
            manifest.Items.Add(new TransferItem
            {
                ItemIndex = index++,
                RelativePath = file.RelativePath,
                Size = file.SourceSize,
                IsDirectory = false,
                LastWriteTimeUtc = file.SourceLastWriteUtc ?? DateTime.UtcNow,
                ExpectedSha256 = file.SourceHash
            });
        }

        TransferManifest = manifest;
        return manifest;
    }

    public Dictionary<string, string> GetLocalPathMap()
    {
        if (SourceManifest == null || string.IsNullOrEmpty(SourceManifest.RootPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ComparisonResults.Where(r => r.Result == FileComparisonResult.New || r.Result == FileComparisonResult.Changed))
        {
            string fullPath = Path.Combine(SourceManifest.RootPath, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            map[item.RelativePath] = fullPath;
        }

        return map;
    }

    public static List<string> SafeDeleteDestinationOnlyFiles(string destinationRoot, IEnumerable<ComparedFile> filesToDelete)
    {
        var deletedFiles = new List<string>();
        string fullDestRoot = Path.GetFullPath(destinationRoot);

        foreach (var file in filesToDelete)
        {
            if (file.Result != FileComparisonResult.DestinationOnly)
                continue;

            // Security check: Path Traversal Prevention
            if (file.RelativePath.Contains("..") || Path.IsPathRooted(file.RelativePath))
            {
                Logger.Warn($"Blocked path traversal attempt in delete: {file.RelativePath}");
                continue;
            }

            string fullPath = Path.GetFullPath(Path.Combine(fullDestRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

            // Ensure target is strictly within the destination directory
            if (!fullPath.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn($"Blocked deletion outside destination root: {fullPath}");
                continue;
            }

            if (File.Exists(fullPath))
            {
                try
                {
                    // Check if reparse point
                    var attr = File.GetAttributes(fullPath);
                    if (attr.HasFlag(FileAttributes.ReparsePoint))
                    {
                        Logger.Warn($"Skipping symlink/junction deletion: {fullPath}");
                        continue;
                    }

                    File.Delete(fullPath);
                    deletedFiles.Add(file.RelativePath);
                    Logger.Info($"Deleted destination-only file: {file.RelativePath}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to delete destination-only file {fullPath}", ex);
                }
            }
        }

        return deletedFiles;
    }
}
