using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FastEthernetTransfer.Core.TransferEngine;

public enum FrameType : byte
{
    Handshake = 1,
    HandshakeAck = 2,
    OfferManifest = 3,
    OfferResponse = 4,
    FileStart = 5,
    DataChunk = 6,
    FileEnd = 7,
    FileAck = 8,
    TransferComplete = 9,
    Pause = 10,
    Resume = 11,
    Cancel = 12,
    BenchmarkStart = 20,
    BenchmarkData = 21,
    BenchmarkEnd = 22,
    GameUpdateFolderRequest = 30,
    GameUpdateFolderResponse = 31,
    GameUpdateManifestRequest = 32,
    GameUpdateManifestResponse = 33,
    GameUpdateStartTransferRequest = 34,
    GameUpdateStartTransferResponse = 35
}

public class HandshakePayload
{
    public int ProtocolVersion { get; set; } = 1;
    public Guid SenderId { get; set; }
    public string SenderDeviceName { get; set; } = string.Empty;
    public int PreferredBufferSize { get; set; } = 2 * 1024 * 1024;
}

public class HandshakeAckPayload
{
    public bool Accepted { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int NegotiatedBufferSize { get; set; } = 2 * 1024 * 1024;
}

public class OfferResponsePayload
{
    public bool Accepted { get; set; }
    public string? Reason { get; set; }
    public string DestinationFolder { get; set; } = string.Empty;
    // Map of ItemIndex -> ResumeOffset (bytes already present on destination)
    public System.Collections.Generic.Dictionary<int, long> ResumeOffsets { get; set; } = [];
}

public static class TransferProtocol
{
    public static readonly byte[] MagicBytes = [0x46, 0x45, 0x54, 0x31]; // "FET1"
    public const int HeaderSize = 9; // 4 bytes magic + 1 byte type + 4 bytes length

    public static async Task WriteFrameAsync(Stream stream, FrameType type, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        byte[] header = ArrayPool<byte>.Shared.Rent(HeaderSize);
        try
        {
            MagicBytes.CopyTo(header, 0);
            header[4] = (byte)type;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(5, 4), payload.Length);

            await stream.WriteAsync(header.AsMemory(0, HeaderSize), ct).ConfigureAwait(false);
            if (payload.Length > 0)
            {
                await stream.WriteAsync(payload, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    public static async Task WriteJsonFrameAsync<T>(Stream stream, FrameType type, T obj, CancellationToken ct)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(obj);
        await WriteFrameAsync(stream, type, jsonBytes, ct).ConfigureAwait(false);
    }

    public static async Task<(FrameType Type, int Length)> ReadHeaderAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = ArrayPool<byte>.Shared.Rent(HeaderSize);
        try
        {
            await ReadExactAsync(stream, header.AsMemory(0, HeaderSize), ct).ConfigureAwait(false);

            // Validate magic
            if (header[0] != MagicBytes[0] || header[1] != MagicBytes[1] ||
                header[2] != MagicBytes[2] || header[3] != MagicBytes[3])
            {
                throw new InvalidDataException("Invalid protocol magic bytes. Connection rejected.");
            }

            FrameType type = (FrameType)header[4];
            int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(5, 4));
            if (length < 0)
            {
                throw new InvalidDataException("Negative frame payload length.");
            }

            return (type, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    public static async Task<T?> ReadJsonPayloadAsync<T>(Stream stream, int length, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            await ReadExactAsync(stream, buffer.AsMemory(0, length), ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int totalRead = 0;
        int required = buffer.Length;

        while (totalRead < required)
        {
            int read = await stream.ReadAsync(buffer.Slice(totalRead, required - totalRead), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException($"Connection closed prematurely. Read {totalRead}/{required} bytes.");
            }
            totalRead += read;
        }
    }

    // High performance binary chunk writer directly onto stream:
    // Header (9 bytes) + ItemIndex (4 bytes) + Offset (8 bytes) + Length (4 bytes) + Payload (Length bytes)
    public static async Task WriteDataChunkAsync(Stream stream, int itemIndex, long offset, ReadOnlyMemory<byte> chunkData, CancellationToken ct)
    {
        int chunkHeaderSize = HeaderSize + 4 + 8 + 4; // 25 bytes
        byte[] header = ArrayPool<byte>.Shared.Rent(chunkHeaderSize);
        try
        {
            MagicBytes.CopyTo(header, 0);
            header[4] = (byte)FrameType.DataChunk;
            int totalPayloadLength = 16 + chunkData.Length;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(5, 4), totalPayloadLength);

            // Chunk metadata inside frame
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(9, 4), itemIndex);
            BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(13, 8), offset);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(21, 4), chunkData.Length);

            await stream.WriteAsync(header.AsMemory(0, chunkHeaderSize), ct).ConfigureAwait(false);
            if (chunkData.Length > 0)
            {
                await stream.WriteAsync(chunkData, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }
}
