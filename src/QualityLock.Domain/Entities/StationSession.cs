using QualityLock.Shared.Enums;

namespace QualityLock.Domain.Entities;

public class StationSession
{
    public Guid Id { get; init; }
    public int StationId { get; init; }
    public int OperatorId { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; set; }
    public SessionStatus Status { get; set; }
    public bool StartedOnline { get; init; }
    public bool EndedOnline { get; set; }
    public string CorrelationId { get; init; } = string.Empty;
}
