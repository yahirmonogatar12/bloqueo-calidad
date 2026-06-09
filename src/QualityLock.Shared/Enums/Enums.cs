namespace QualityLock.Shared.Enums;

public enum StationType
{
    ICT,
    FCT,
    Packing
}

public enum SessionStatus
{
    Open,
    Closed,
    ForcedClosed,
    OfflinePending
}

public enum StationEventType
{
    LockShown,
    BadgeScanned,
    UnlockGranted,
    UnlockDenied,
    AutoLock,
    AdminPanelOpened,
    AdminOverrideApproved,
    AdminOverrideRejected,
    BypassUsed,
    SafeModeEntered,
    HeartbeatSent,
    ClientRecovered
}

public enum OverrideReasonType
{
    OperatorAbsent,
    SystemError,
    Maintenance,
    Emergency,
    Other
}

public enum ValidationDecision
{
    Allowed,
    Denied,
    OfflineAllowed
}
