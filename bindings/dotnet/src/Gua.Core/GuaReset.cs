namespace Gua.Core;

[Flags]
public enum GuaResetTargets : uint
{
    None = 0,
    Nodes = 1 << 0,
    Requests = 1 << 1,
    Events = 1 << 2,
    History = 1 << 3,
    Logs = 1 << 4,
    Screenshot = 1 << 5,
    Default = Nodes | Requests | Events | History,
    All = Default | Logs | Screenshot,
}

public enum GuaResetResult
{
    Succeeded = 1,
    InvalidArgument = -1,
    Dirty = -2,
    StaleEpoch = -3,
}

public sealed record GuaResetOptions(
    GuaResetTargets Targets = GuaResetTargets.Default,
    bool Strict = false,
    ulong? ExpectedSessionEpoch = null);

public sealed record GuaContextStatus(
    ulong SessionEpoch,
    ulong FrameSequence,
    ulong Revision,
    uint NodeCount,
    uint PendingRequestCount,
    uint InFlightRequestCount,
    uint UnconsumedEventCount,
    uint LogCount,
    bool HasScreenshot,
    GuaActionType? FirstPendingAction,
    string FirstPendingNodeId,
    GuaActionType? FirstEventAction,
    string FirstEventNodeId)
{
    public bool IsClean => PendingRequestCount == 0 && InFlightRequestCount == 0 && UnconsumedEventCount == 0;
}

public sealed record GuaResetReport(
    GuaResetResult Result,
    ulong PreviousSessionEpoch,
    ulong SessionEpoch,
    uint PendingRequestCount,
    uint InFlightRequestCount,
    uint UnconsumedEventCount,
    uint DiscardedNodeCount,
    uint DiscardedPendingRequestCount,
    uint DiscardedInFlightRequestCount,
    uint DiscardedEventCount,
    uint DiscardedLogCount,
    bool DiscardedScreenshot,
    GuaActionType? FirstPendingAction,
    string FirstPendingNodeId,
    GuaActionType? FirstEventAction,
    string FirstEventNodeId);
