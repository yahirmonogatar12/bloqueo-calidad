using Dapper;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class HeartbeatRepository(IDbConnectionFactory factory) : IHeartbeatRepository
{
    public async Task InsertAsync(ClientHeartbeat heartbeat, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO client_heartbeats_QA
                (id, station_id, sent_at_utc, client_version, is_safe_mode, last_activity_at_utc, details_json)
            VALUES
                (@Id, @StationId, @SentAtUtc, @ClientVersion, @IsSafeMode, @LastActivityAtUtc, @DetailsJson)
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, heartbeat);
    }
}
