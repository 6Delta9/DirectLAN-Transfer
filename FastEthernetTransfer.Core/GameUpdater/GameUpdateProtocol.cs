using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FastEthernetTransfer.Core.Models;

namespace FastEthernetTransfer.Core.GameUpdater;

public class GameUpdateFolderRequestPayload
{
    public string RequestId { get; set; } = string.Empty;
    public string RequesterDeviceName { get; set; } = string.Empty;
}

public class GameUpdateFolderRequest
{
    public string RequestId { get; init; } = string.Empty;
    public string RequesterDeviceName { get; init; } = string.Empty;
    public string RequesterIp { get; init; } = string.Empty;
    public TaskCompletionSource<(bool accepted, string? folderPath)> DecisionSource { get; } = new();
}

public class GameUpdateFolderResponsePayload
{
    public string RequestId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string? SelectedFolderPath { get; set; }
    public string? Reason { get; set; }
}

public class GameUpdateManifestRequestPayload
{
    public string RequestId { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
}

public class GameUpdateManifestResponsePayload
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public FolderManifest? Manifest { get; set; }
    public string? Error { get; set; }
}

public class GameUpdateStartTransferRequestPayload
{
    public string RequestId { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public int TargetPort { get; set; } = 45779;
    public string SourceFolderPath { get; set; } = string.Empty;
    public string DestinationFolderPath { get; set; } = string.Empty;
    public List<string> RelativePathsToTransfer { get; set; } = [];
}

public class GameUpdateStartTransferResponsePayload
{
    public string RequestId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string? Reason { get; set; }
}
