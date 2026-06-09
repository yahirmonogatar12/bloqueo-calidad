using QualityLock.Shared.Enums;

namespace QualityLock.Domain.Entities;

public class StationEvent
{
    public Guid Id { get; init; }
    public int StationId { get; init; }
    public int? OperatorId { get; init; }
    public Guid? SessionId { get; init; }
    public StationEventType EventType { get; init; }
    public DateTime EventAtUtc { get; init; }
    public string? DetailsJson { get; init; }
    public string Source { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
}
