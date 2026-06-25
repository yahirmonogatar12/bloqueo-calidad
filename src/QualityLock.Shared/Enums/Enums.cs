namespace QualityLock.Shared.Enums;

public enum StationType
{
    ICT,
    FCT,
    Packing,
    Vision
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
    ClientRecovered,
    WindowAuthorized,
    WindowClosed
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
