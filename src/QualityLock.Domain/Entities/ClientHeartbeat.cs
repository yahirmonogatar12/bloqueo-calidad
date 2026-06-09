namespace QualityLock.Domain.Entities;

public class ClientHeartbeat
{
    public Guid Id { get; init; }
    public int StationId { get; init; }
    public DateTime SentAtUtc { get; init; }
    public string ClientVersion { get; init; } = string.Empty;
    public bool IsSafeMode { get; init; }
    public DateTime LastActivityAtUtc { get; init; }
    public string? DetailsJson { get; init; }
}
