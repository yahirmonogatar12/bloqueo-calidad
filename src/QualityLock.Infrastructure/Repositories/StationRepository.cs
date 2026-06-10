using Dapper;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class StationRepository(IDbConnectionFactory factory) : IStationRepository
{
    public async Task<Station?> GetByCodeAndLineAsync(string stationCode, string line, CancellationToken ct = default)
    {
        // Si la linea viene vacia, se busca solo por codigo (toma la primera coincidencia).
        // Con linea, la combinacion (codigo, linea) es unica.
        var sql = string.IsNullOrWhiteSpace(line)
            ? """
              SELECT id AS Id, station_code AS StationCode, station_name AS StationName,
                     station_type AS StationType, host_name AS HostName, is_active AS IsActive,
                     created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc
              FROM stations_QA WHERE station_code = @StationCode LIMIT 1
              """
            : """
              SELECT id AS Id, station_code AS StationCode, station_name AS StationName,
                     station_type AS StationType, host_name AS HostName, is_active AS IsActive,
                     created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc
              FROM stations_QA WHERE station_code = @StationCode AND host_name = @Line LIMIT 1
              """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<Station>(sql, new { StationCode = stationCode, Line = line });
    }

    public async Task<int> UpsertAsync(Station station, CancellationToken ct = default)
    {
        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);

        if (station.Id == 0)
        {
            const string insert = """
                INSERT INTO stations_QA (station_code, station_name, station_type, host_name, is_active)
                VALUES (@StationCode, @StationName, @StationType, @HostName, @IsActive);
                SELECT LAST_INSERT_ID();
                """;
            return await conn.ExecuteScalarAsync<int>(insert, new
            {
                station.StationCode,
                station.StationName,
                StationType = station.StationType.ToString(),
                station.HostName,
                station.IsActive
            });
        }
        else
        {
            const string update = """
                UPDATE stations_QA
                SET station_name = @StationName,
                    station_type = @StationType,
                    host_name    = @HostName,
                    is_active    = @IsActive
                WHERE id = @Id;
                SELECT @Id;
                """;
            return await conn.ExecuteScalarAsync<int>(update, new
            {
                station.Id,
                station.StationName,
                StationType = station.StationType.ToString(),
                station.HostName,
                station.IsActive
            });
        }
    }
}
