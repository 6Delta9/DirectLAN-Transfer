using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;

namespace FastEthernetTransfer.Core.GameUpdater;

public class FileFingerprint
{
    public required string RelativePath { get; set; }
    public long Size { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string? Sha256Hash { get; set; }
}

public class FolderManifest
{
    public string RootPath { get; set; } = string.Empty;
    public Dictionary<string, FileFingerprint> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
    public DateTime ScanTimeUtc { get; set; } = DateTime.UtcNow;

    public static async Task<FolderManifest> ScanFolderAsync(
        string rootPath,
        IProgress<(int scanned, string currentFile)>? progress = null,
        CancellationToken ct = default,
        bool computeHashes = false)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");
        }

        var manifest = new FolderManifest
        {
            RootPath = rootPath,
            ScanTimeUtc = DateTime.UtcNow
        };

        var rootDirInfo = new DirectoryInfo(rootPath);
        int scannedCount = 0;
        long totalBytes = 0;

        // Recursive directory traversal ignoring junctions/symlinks
        var dirsToVisit = new Stack<DirectoryInfo>();
        dirsToVisit.Push(rootDirInfo);

        while (dirsToVisit.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var currentDir = dirsToVisit.Pop();

            // Enumerate subdirectories
            try
            {
                foreach (var subDir in currentDir.EnumerateDirectories())
                {
                    // Skip symbolic links, reparse points, and junctions
                    if (subDir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        Logger.Info($"Skipping reparse point / junction: {subDir.FullName}");
                        continue;
                    }

                    dirsToVisit.Push(subDir);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not enumerate directories in {currentDir.FullName}: {ex.Message}");
            }

            // Enumerate files
            try
            {
                foreach (var file in currentDir.EnumerateFiles())
                {
                    ct.ThrowIfCancellationRequested();

                    // Skip reparse points
                    if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;

                    string relativePath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');

                    string? hash = null;
                    if (computeHashes)
                    {
                        try
                        {
                            hash = await FileHasher.ComputeHashAsync(file.FullName, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Could not hash file {file.FullName}: {ex.Message}");
                        }
                    }

                    var fingerprint = new FileFingerprint
                    {
                        RelativePath = relativePath,
                        Size = file.Length,
                        LastWriteUtc = file.LastWriteTimeUtc,
                        Sha256Hash = hash
                    };

                    manifest.Files[relativePath] = fingerprint;
                    totalBytes += file.Length;
                    scannedCount++;

                    progress?.Report((scannedCount, relativePath));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not enumerate files in {currentDir.FullName}: {ex.Message}");
            }
        }

        manifest.TotalBytes = totalBytes;
        manifest.TotalFiles = manifest.Files.Count;

        return manifest;
    }
}
