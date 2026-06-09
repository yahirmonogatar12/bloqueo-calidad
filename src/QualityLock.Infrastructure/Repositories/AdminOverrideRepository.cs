using Dapper;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class AdminOverrideRepository(IDbConnectionFactory factory) : IAdminOverrideRepository
{
    public async Task<Guid> CreateAsync(AdminOverride adminOverride, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO admin_overrides_QA
                (id, station_id, admin_operator_id, target_operator_id, reason, comments, approved, created_at_utc)
            VALUES
                (@Id, @StationId, @AdminOperatorId, @TargetOperatorId, @Reason, @Comments, @Approved, @CreatedAtUtc)
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            adminOverride.Id,
            adminOverride.StationId,
            adminOverride.AdminOperatorId,
            adminOverride.TargetOperatorId,
            Reason = adminOverride.Reason.ToString(),
            adminOverride.Comments,
            adminOverride.Approved,
            adminOverride.CreatedAtUtc
        });

        return adminOverride.Id;
    }
}
