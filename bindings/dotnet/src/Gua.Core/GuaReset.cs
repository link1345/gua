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
    Clock = 1 << 6,
    WorldObjects = 1 << 7,
    Default = Nodes | Requests | Events | History,
    LegacySessionDefault = Default | Clock,
    SessionDefault = LegacySessionDefault | WorldObjects,
    All = Default | Logs | Screenshot,
    /// <summary>Legacy full-reset mask retained for compatibility; does not include World Object Tree state.</summary>
    AllWithClock = All | Clock,
    /// <summary>Current full-reset mask including clock and World Object Tree state.</summary>
    AllCurrent = AllWithClock | WorldObjects,
}

public enum GuaResetResult
{
    Succeeded = 1,
    InvalidArgument = -1,
    Dirty = -2,
    StaleEpoch = -3,
}

public sealed record GuaResetOptions(
    GuaResetTargets Targets = GuaResetTargets.SessionDefault,
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
    public ulong WorldFrameSequence { get; init; }
    public ulong WorldRevision { get; init; }
    public uint WorldObjectCount { get; init; }
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
    string FirstEventNodeId)
{
    public uint DiscardedWorldObjectCount { get; init; }
}
