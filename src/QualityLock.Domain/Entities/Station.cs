using QualityLock.Shared.Enums;

namespace QualityLock.Domain.Entities;

public class Station
{
    public int Id { get; init; }
    public string StationCode { get; init; } = string.Empty;
    public string StationName { get; init; } = string.Empty;
    public StationType StationType { get; init; }
    public string HostName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
