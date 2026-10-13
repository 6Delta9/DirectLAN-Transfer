using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FastEthernetTransfer.Core.Diagnostics;

namespace FastEthernetTransfer.Core.TransferEngine;

public record ValidatedTransferItem(string FullPath, string Name, bool IsDirectory, long SizeBytes);

public static class DragDropItemProcessor
{
    public static List<ValidatedTransferItem> ProcessDroppedPaths(IEnumerable<string>? rawPaths)
    {
        if (rawPaths == null) return [];

        var results = new List<ValidatedTransferItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawPaths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(raw.Trim());
            }
            catch
            {
                // Invalid path format or forbidden characters
                continue;
            }

            // Path Traversal and Existence Security Checks
            if (fullPath.Contains("..")) continue;

            if (seenPaths.Contains(fullPath)) continue;

            if (File.Exists(fullPath))
            {
                try
                {
                    var fi = new FileInfo(fullPath);
                    seenPaths.Add(fullPath);
                    results.Add(new ValidatedTransferItem(
                        fullPath,
                        fi.Name,
                        false,
                        fi.Length
                    ));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not access dropped file {fullPath}: {ex.Message}");
                }
            }
            else if (Directory.Exists(fullPath))
            {
                try
                {
                    var di = new DirectoryInfo(fullPath);
                    seenPaths.Add(fullPath);
                    
                    // Quick shallow or streaming size calculation without loading into RAM
                    long folderSize = CalculateFolderSizeSafe(di);
                    results.Add(new ValidatedTransferItem(
                        fullPath,
                        di.Name,
                        true,
                        folderSize
                    ));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not access dropped directory {fullPath}: {ex.Message}");
                }
            }
        }

        return results;
    }

    private static long CalculateFolderSizeSafe(DirectoryInfo dir)
    {
        long total = 0;
        try
        {
            foreach (var fi in dir.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                total += fi.Length;
            }
        }
        catch
        {
            // Inaccessible folder or enumeration limit
        }
        return total;
    }
}
