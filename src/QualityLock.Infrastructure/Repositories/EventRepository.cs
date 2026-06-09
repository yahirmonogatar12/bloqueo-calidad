using Dapper;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class EventRepository(IDbConnectionFactory factory) : IEventRepository
{
    public Task InsertAsync(StationEvent evt, CancellationToken ct = default)
        => InsertBatchAsync([evt], ct);

    public async Task InsertBatchAsync(IEnumerable<StationEvent> events, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO station_events_QA
                (id, station_id, operator_id, session_id, event_type, event_at_utc, details_json, source, correlation_id)
            VALUES
                (@Id, @StationId, @OperatorId, @SessionId, @EventType, @EventAtUtc, @DetailsJson, @Source, @CorrelationId)
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var evt in events)
        {
            await conn.ExecuteAsync(sql, new
            {
                evt.Id,
                evt.StationId,
                evt.OperatorId,
                evt.SessionId,
                EventType = evt.EventType.ToString(),
                evt.EventAtUtc,
                evt.DetailsJson,
                evt.Source,
                evt.CorrelationId
            }, tx);
        }

        await tx.CommitAsync(ct);
    }
}
