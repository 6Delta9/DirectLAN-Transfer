using System;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.GameUpdater;

public class ComparedFile
{
    public required string RelativePath { get; init; }
    public long SourceSize { get; init; }
    public long DestinationSize { get; init; }
    public DateTime? SourceLastWriteUtc { get; init; }
    public DateTime? DestinationLastWriteUtc { get; init; }
    public string? SourceHash { get; init; }
    public string? DestinationHash { get; init; }
    public FileComparisonResult Result { get; init; }
    public string? ErrorMessage { get; init; }

    public string SizeFormatted => TransferProgress.FormatBytes(SourceSize > 0 ? SourceSize : DestinationSize);
}
