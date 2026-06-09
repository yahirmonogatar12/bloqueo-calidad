namespace QualityLock.Domain.Entities;

public class StationPermission
{
    public int OperatorId { get; init; }
    public int StationId { get; init; }
    public bool CanOperate { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
