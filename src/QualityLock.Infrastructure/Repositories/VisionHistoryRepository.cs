using Dapper;
using MySqlConnector;
using QualityLock.Application.Interfaces;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class VisionHistoryRepository(IDbConnectionFactory factory) : IVisionHistoryRepository
{
    public async Task<VisionInspection?> GetLastAsync(string barcode, CancellationToken ct = default)
    {
        // Ordena por log_datetime (cuando se inspecciono) y no por captured_at_utc (cuando se
        // cargo): el watcher de Vision reingesta logs viejos y eso no debe tapar una
        // reinspeccion mas nueva. idx_history_vision_barcode_captured resuelve el WHERE; el
        // ORDER BY solo ordena las pocas filas de ese barcode.
        const string sql = """
            SELECT source_uid AS SourceUid, result AS Result, log_datetime AS InspectedAt, machine_name AS Machine
            FROM history_vision
            WHERE barcode = @Barcode
            ORDER BY log_datetime DESC, id DESC
            LIMIT 1
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<VisionInspection>(sql, new { Barcode = barcode });
    }

    public async Task<IReadOnlyList<VisionDefect>> GetDefectsAsync(string sourceUid, CancellationToken ct = default)
    {
        const string sql = """
            SELECT test_name AS TestName, position AS Position, sub_test AS SubTest,
                   value_text AS ValueText, value_num AS ValueNum
            FROM history_vision_defects
            WHERE source_uid = @SourceUid
            ORDER BY id
            LIMIT 10
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        try
        {
            var rows = await conn.QueryAsync<VisionDefect>(sql, new { SourceUid = sourceUid });
            return rows.ToList();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.NoSuchTable)
        {
            // La crea el watcher de Vision la primera vez que guarda un NG del SHT.
            return [];
        }
    }
}
