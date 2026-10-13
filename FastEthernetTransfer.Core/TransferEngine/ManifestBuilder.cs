using System;
using System.Collections.Generic;
using System.IO;
using FastEthernetTransfer.Core.Diagnostics;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.TransferEngine;

public static class ManifestBuilder
{
    public static TransferManifest BuildFromPaths(
        IEnumerable<string> filePaths,
        IEnumerable<string> folderPaths,
        Guid senderId,
        string senderDeviceName)
    {
        var manifest = new TransferManifest
        {
            SenderId = senderId,
            SenderDeviceName = senderDeviceName
        };

        int index = 0;
        long totalBytes = 0;
        int totalFiles = 0;
        int totalFolders = 0;

        // 1. Process individual files
        if (filePaths != null)
        {
            foreach (var filePath in filePaths)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists) continue;

                    manifest.Items.Add(new TransferItem
                    {
                        ItemIndex = index++,
                        RelativePath = fileInfo.Name, // top-level file name
                        Size = fileInfo.Length,
                        IsDirectory = false,
                        LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
                    });

                    totalBytes += fileInfo.Length;
                    totalFiles++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error reading file info: {filePath}", ex);
                }
            }
        }

        // 2. Process folders recursively
        if (folderPaths != null)
        {
            foreach (var folderPath in folderPaths)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(folderPath);
                    if (!dirInfo.Exists) continue;

                    string rootDirName = dirInfo.Name;
                    totalFolders++;

                    // Add top-level folder marker
                    manifest.Items.Add(new TransferItem
                    {
                        ItemIndex = index++,
                        RelativePath = NormalizePath(rootDirName),
                        Size = 0,
                        IsDirectory = true,
                        LastWriteTimeUtc = dirInfo.LastWriteTimeUtc
                    });

                    // Add all subdirectories (including empty ones)
                    foreach (var subDir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string relativeSub = Path.GetRelativePath(dirInfo.FullName, subDir.FullName);
                            string combinedPath = Path.Combine(rootDirName, relativeSub);

                            manifest.Items.Add(new TransferItem
                            {
                                ItemIndex = index++,
                                RelativePath = NormalizePath(combinedPath),
                                Size = 0,
                                IsDirectory = true,
                                LastWriteTimeUtc = subDir.LastWriteTimeUtc
                            });

                            totalFolders++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Could not access directory {subDir.FullName}: {ex.Message}");
                        }
                    }

                    // Add all files within directory
                    foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string relativeFile = Path.GetRelativePath(dirInfo.FullName, file.FullName);
                            string combinedPath = Path.Combine(rootDirName, relativeFile);

                            manifest.Items.Add(new TransferItem
                            {
                                ItemIndex = index++,
                                RelativePath = NormalizePath(combinedPath),
                                Size = file.Length,
                                IsDirectory = false,
                                LastWriteTimeUtc = file.LastWriteTimeUtc
                            });

                            totalBytes += file.Length;
                            totalFiles++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Could not access file {file.FullName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error scanning folder: {folderPath}", ex);
                }
            }
        }

        manifest.TotalBytes = totalBytes;
        manifest.TotalFiles = totalFiles;
        manifest.TotalFolders = totalFolders;

        return manifest;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
