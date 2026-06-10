using QualityLock.Shared.Enums;

namespace QualityLock.Shared.DTOs;

public record BadgeValidationRequest(
    string StationCode,
    string BadgeCode,
    DateTime ClientUtc,
    string Line = "");

public record BadgeValidationResponse(
    string BadgeCode,
    string EmployeeNumber,
    string DisplayName,
    ValidationDecision Decision,
    string? DenyReason,
    bool CanOperate,
    StationSnapshot Station,
    string? Role = null);

public record StationSnapshot(
    string StationCode,
    string StationName,
    StationType StationType,
    bool IsActive);

public record StartSessionRequest(
    string StationCode,
    string BadgeCode,
    DateTime ClientUtc,
    bool IsOnline,
    string CorrelationId,
    string Line = "");

public record StartSessionResponse(
    Guid SessionId,
    DateTime StartedAtUtc);

public record EndSessionRequest(
    Guid SessionId,
    string StationCode,
    string Reason,
    DateTime ClientUtc,
    bool IsOnline,
    string Line = "");

public record StationEventRequest(
    string StationCode,
    string? BadgeCode,
    Guid? SessionId,
    StationEventType EventType,
    DateTime EventAtUtc,
    string? DetailsJson,
    string Source,
    string CorrelationId,
    string Line = "");

public record AdminOverrideRequest(
    string StationCode,
    string AdminBadgeCode,
    string? TargetBadgeCode,
    OverrideReasonType Reason,
    string Comments,
    DateTime ClientUtc,
    string Line = "");

public record AdminOverrideResponse(
    Guid OverrideId,
    bool Approved,
    string Message);

public record HeartbeatRequest(
    string StationCode,
    DateTime SentAtUtc,
    string ClientVersion,
    bool IsSafeMode,
    DateTime LastActivityAtUtc,
    string? DetailsJson,
    string Line = "");

public record StationBootstrapResponse(
    StationSnapshot Station,
    IReadOnlyList<CachedOperatorDto> AllowedOperators,
    DateTime GeneratedAtUtc);

public record CachedOperatorDto(
    string BadgeCode,
    string EmployeeNumber,
    string DisplayName,
    bool IsAdmin,
    string? Role = null);

public record ApiErrorDto(
    string Code,
    string Message,
    string? CorrelationId = null);

public record RegisterStationRequest(
    string StationCode,
    string StationName,
    StationType StationType,
    string HostName,
    bool IsActive);

public record RegisterStationResponse(
    int StationId,
    string StationCode,
    bool Created,
    string Message);

public record TokenRequest(
    string StationCode,
    string ApiKey);

public record TokenResponse(
    string AccessToken,
    DateTime ExpiresAtUtc);

/// <summary>Login de administrador contra usuarios_sistema (username + password).</summary>
public record AdminLoginRequest(
    string Username,
    string Password);

public record AdminLoginResponse(
    bool Authenticated,
    bool IsAdmin,
    string Username,
    string DisplayName,
    string? Departamento,
    string? Cargo,
    string? Message,
    string? Role = null,
    bool CanUnlockManually = false);
