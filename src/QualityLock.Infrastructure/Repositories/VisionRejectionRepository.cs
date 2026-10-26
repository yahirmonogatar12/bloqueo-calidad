using Dapper;
using QualityLock.Application.Interfaces;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class VisionRejectionRepository(IDbConnectionFactory factory) : IVisionRejectionRepository
{
    public async Task AddAsync(VisionRejection r, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO vision_check_rejections_QA
                (rejected_at_utc, station_code, line, code, rejection_type, reason, machine, inspected_at)
            VALUES
                (@RejectedAtUtc, @StationCode, @Line, @Code, @Type, @Reason, @Machine, @InspectedAt)
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        // Recorta a los VARCHAR de la tabla: en modo estricto un texto largo haria fallar el INSERT.
        await conn.ExecuteAsync(sql, r with
        {
            StationCode = Cut(r.StationCode, 64),
            Line = Cut(r.Line, 64),
            Code = Cut(r.Code, 128),
            Reason = Cut(r.Reason, 512),
        });
    }

    private static string Cut(string? value, int max)
        => string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max];
}
