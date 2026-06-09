using Dapper;
using MySqlConnector;
using QualityLock.Application.Exceptions;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class SessionRepository(IDbConnectionFactory factory) : ISessionRepository
{
    private const int MySqlDuplicateEntry = 1062;

    private const string InsertSessionSql = """
        INSERT INTO station_sessions_QA
            (id, station_id, operator_id, started_at_utc, status, started_online, ended_online, correlation_id)
        VALUES
            (@Id, @StationId, @OperatorId, @StartedAtUtc, @Status, @StartedOnline, 0, @CorrelationId)
        """;

    private const string InsertEventSql = """
        INSERT INTO station_events_QA
            (id, station_id, operator_id, session_id, event_type, event_at_utc, details_json, source, correlation_id)
        VALUES
            (@Id, @StationId, @OperatorId, @SessionId, @EventType, @EventAtUtc, @DetailsJson, @Source, @CorrelationId)
        """;

    public async Task<Guid> CreateAsync(StationSession session, CancellationToken ct = default)
    {
        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(InsertSessionSql, SessionParams(session));
        return session.Id;
    }

    public async Task CreateWithUnlockEventAsync(
        StationSession session, StationEvent unlockEvent, CancellationToken ct = default)
    {
        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(InsertSessionSql, SessionParams(session), tx);
            await conn.ExecuteAsync(InsertEventSql, new
            {
                unlockEvent.Id,
                unlockEvent.StationId,
                unlockEvent.OperatorId,
                unlockEvent.SessionId,
                EventType = unlockEvent.EventType.ToString(),
                unlockEvent.EventAtUtc,
                unlockEvent.DetailsJson,
                unlockEvent.Source,
                unlockEvent.CorrelationId
            }, tx);

            await tx.CommitAsync(ct);
        }
        catch (MySqlException ex) when (ex.Number == MySqlDuplicateEntry)
        {
            await tx.RollbackAsync(ct);
            // The unique open-session index rejected a concurrent duplicate.
            throw new ConflictException(
                "Station already has an open session (rejected by database constraint).");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static object SessionParams(StationSession session) => new
    {
        session.Id,
        session.StationId,
        session.OperatorId,
        session.StartedAtUtc,
        Status = session.Status.ToString(),
        session.StartedOnline,
        session.CorrelationId
    };

    public async Task<StationSession?> GetOpenSessionByStationAsync(int stationId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id               AS Id,
                   station_id       AS StationId,
                   operator_id      AS OperatorId,
                   started_at_utc   AS StartedAtUtc,
                   ended_at_utc     AS EndedAtUtc,
                   status           AS Status,
                   started_online   AS StartedOnline,
                   ended_online     AS EndedOnline,
                   correlation_id   AS CorrelationId
            FROM station_sessions_QA
            WHERE station_id = @StationId
              AND ended_at_utc IS NULL
            LIMIT 1
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<StationSession>(sql, new { StationId = stationId });
    }

    public async Task CloseAsync(Guid sessionId, DateTime endedAtUtc, string status, bool endedOnline, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE station_sessions_QA
            SET ended_at_utc = @EndedAtUtc,
                status = @Status,
                ended_online = @EndedOnline
            WHERE id = @SessionId
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { SessionId = sessionId, EndedAtUtc = endedAtUtc, Status = status, EndedOnline = endedOnline });
    }
}
