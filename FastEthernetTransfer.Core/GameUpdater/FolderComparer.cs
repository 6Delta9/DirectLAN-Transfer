using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;

namespace FastEthernetTransfer.Core.GameUpdater;

public static class FolderComparer
{
    public static async Task<List<ComparedFile>> CompareAsync(
        FolderManifest source,
        FolderManifest destination,
        IProgress<(int compared, int total, string currentFile)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<ComparedFile>();
        var allRelativePaths = new HashSet<string>(source.Files.Keys, StringComparer.OrdinalIgnoreCase);
        allRelativePaths.UnionWith(destination.Files.Keys);

        int totalCount = allRelativePaths.Count;
        int processedCount = 0;

        foreach (var relPath in allRelativePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            bool inSource = source.Files.TryGetValue(relPath, out var sourceFile);
            bool inDest = destination.Files.TryGetValue(relPath, out var destFile);

            if (inSource && !inDest && sourceFile != null)
            {
                // New file (exists on source only)
                results.Add(new ComparedFile
                {
                    RelativePath = relPath,
                    SourceSize = sourceFile.Size,
                    DestinationSize = 0,
                    SourceLastWriteUtc = sourceFile.LastWriteUtc,
                    DestinationLastWriteUtc = null,
                    SourceHash = sourceFile.Sha256Hash,
                    DestinationHash = null,
                    Result = FileComparisonResult.New
                });
            }
            else if (!inSource && inDest && destFile != null)
            {
                // Destination-only file
                results.Add(new ComparedFile
                {
                    RelativePath = relPath,
                    SourceSize = 0,
                    DestinationSize = destFile.Size,
                    SourceLastWriteUtc = null,
                    DestinationLastWriteUtc = destFile.LastWriteUtc,
                    SourceHash = null,
                    DestinationHash = destFile.Sha256Hash,
                    Result = FileComparisonResult.DestinationOnly
                });
            }
            else if (inSource && inDest && sourceFile != null && destFile != null)
            {
                // File exists in both
                if (sourceFile.Size != destFile.Size)
                {
                    // Different sizes => definitely changed!
                    results.Add(new ComparedFile
                    {
                        RelativePath = relPath,
                        SourceSize = sourceFile.Size,
                        DestinationSize = destFile.Size,
                        SourceLastWriteUtc = sourceFile.LastWriteUtc,
                        DestinationLastWriteUtc = destFile.LastWriteUtc,
                        SourceHash = sourceFile.Sha256Hash,
                        DestinationHash = destFile.Sha256Hash,
                        Result = FileComparisonResult.Changed
                    });
                }
                else
                {
                    // Same size => verify content hash
                    string? sourceHash = sourceFile.Sha256Hash;
                    string? destHash = destFile.Sha256Hash;
                    string? errorMessage = null;

                    try
                    {
                        // Calculate source hash if missing and local path exists
                        if (string.IsNullOrEmpty(sourceHash) && !string.IsNullOrEmpty(source.RootPath))
                        {
                            string fullSourcePath = Path.Combine(source.RootPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(fullSourcePath))
                            {
                                sourceHash = await FileHasher.ComputeHashAsync(fullSourcePath, ct).ConfigureAwait(false);
                                sourceFile.Sha256Hash = sourceHash;
                            }
                        }

                        // Calculate destination hash if missing and local path exists
                        if (string.IsNullOrEmpty(destHash) && !string.IsNullOrEmpty(destination.RootPath))
                        {
                            string fullDestPath = Path.Combine(destination.RootPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(fullDestPath))
                            {
                                destHash = await FileHasher.ComputeHashAsync(fullDestPath, ct).ConfigureAwait(false);
                                destFile.Sha256Hash = destHash;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error hashing file during comparison: {relPath}", ex);
                        errorMessage = ex.Message;
                    }

                    if (errorMessage != null)
                    {
                        results.Add(new ComparedFile
                        {
                            RelativePath = relPath,
                            SourceSize = sourceFile.Size,
                            DestinationSize = destFile.Size,
                            SourceLastWriteUtc = sourceFile.LastWriteUtc,
                            DestinationLastWriteUtc = destFile.LastWriteUtc,
                            SourceHash = sourceHash,
                            DestinationHash = destHash,
                            Result = FileComparisonResult.Unreadable,
                            ErrorMessage = errorMessage
                        });
                    }
                    else if (!string.IsNullOrEmpty(sourceHash) && !string.IsNullOrEmpty(destHash))
                    {
                        bool isIdentical = string.Equals(sourceHash, destHash, StringComparison.OrdinalIgnoreCase);
                        results.Add(new ComparedFile
                        {
                            RelativePath = relPath,
                            SourceSize = sourceFile.Size,
                            DestinationSize = destFile.Size,
                            SourceLastWriteUtc = sourceFile.LastWriteUtc,
                            DestinationLastWriteUtc = destFile.LastWriteUtc,
                            SourceHash = sourceHash,
                            DestinationHash = destHash,
                            Result = isIdentical ? FileComparisonResult.Identical : FileComparisonResult.Changed
                        });
                    }
                    else
                    {
                        // Fallback if neither hash was computable (e.g. remote peer without hash):
                        // If same size and same timestamp, treat as identical; otherwise changed
                        bool timestampMatch = Math.Abs((sourceFile.LastWriteUtc - destFile.LastWriteUtc).TotalSeconds) < 2;
                        results.Add(new ComparedFile
                        {
                            RelativePath = relPath,
                            SourceSize = sourceFile.Size,
                            DestinationSize = destFile.Size,
                            SourceLastWriteUtc = sourceFile.LastWriteUtc,
                            DestinationLastWriteUtc = destFile.LastWriteUtc,
                            SourceHash = sourceHash,
                            DestinationHash = destHash,
                            Result = timestampMatch ? FileComparisonResult.Identical : FileComparisonResult.Changed
                        });
                    }
                }
            }

            processedCount++;
            progress?.Report((processedCount, totalCount, relPath));
        }

        return results;
    }
}
