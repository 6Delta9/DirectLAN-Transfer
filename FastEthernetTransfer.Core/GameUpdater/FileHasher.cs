using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Diagnostics;

namespace FastEthernetTransfer.Core.GameUpdater;

public static class FileHasher
{
    private const int BufferSize = 1024 * 1024; // 1 MB buffer for fast disk I/O

    public static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found for hashing: {filePath}", filePath);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = BufferSize
            };

            using var stream = new FileStream(filePath, options);
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
            }

            byte[] hashBytes = hash.GetHashAndReset();
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
