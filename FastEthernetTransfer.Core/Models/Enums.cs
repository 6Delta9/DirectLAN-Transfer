using System;

namespace FastEthernetTransfer.Core.Models;

public enum TransferDirection
{
    Send,
    Receive
}

public enum TransferState
{
    Idle,
    Connecting,
    Negotiating,
    Transferring,
    Paused,
    Completed,
    Cancelled,
    Failed
}

public enum ConflictResolution
{
    Replace,
    Skip,
    Rename,
    Ask
}

public enum VerificationMode
{
    Always,
    SkipOver50GB,
    Disabled
}
