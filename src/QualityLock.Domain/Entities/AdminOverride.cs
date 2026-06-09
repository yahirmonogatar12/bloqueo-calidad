using QualityLock.Shared.Enums;

namespace QualityLock.Domain.Entities;

public class AdminOverride
{
    public Guid Id { get; init; }
    public int StationId { get; init; }
    public int AdminOperatorId { get; init; }
    public int? TargetOperatorId { get; init; }
    public OverrideReasonType Reason { get; init; }
    public string Comments { get; init; } = string.Empty;
    public bool Approved { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
