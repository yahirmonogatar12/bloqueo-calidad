namespace QualityLock.Domain.Entities;

public class Operator
{
    public int Id { get; init; }
    public string BadgeCode { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsAdmin { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
