using Dapper;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class OperatorRepository(IDbConnectionFactory factory) : IOperatorRepository
{
    /// <summary>
    /// Crea o actualiza una fila puente en <c>operators_QA</c> que representa a un usuario
    /// de <c>usuarios_sistema</c>. La llave estable es <c>badge_code = username</c> y
    /// <c>employee_number = "USR-{id}"</c> (ambas UNIQUE). Idempotente.
    /// </summary>
    public async Task<Operator> EnsureBridgeOperatorAsync(SystemUser user, bool isAdmin, CancellationToken ct = default)
    {
        const string upsertSql = """
            INSERT INTO operators_QA (badge_code, employee_number, display_name, is_active, is_admin)
            VALUES (@BadgeCode, @EmployeeNumber, @DisplayName, @IsActive, @IsAdmin)
            ON DUPLICATE KEY UPDATE
                display_name = VALUES(display_name),
                is_active    = VALUES(is_active),
                is_admin     = VALUES(is_admin)
            """;

        var badgeCode = user.Username.Trim();
        var employeeNumber = $"USR-{user.Id}";

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(upsertSql, new
        {
            BadgeCode = badgeCode,
            EmployeeNumber = employeeNumber,
            DisplayName = user.DisplayName,
            IsActive = user.Activo,
            IsAdmin = isAdmin
        });

        // Re-leemos por badge_code para obtener el id auto-incremental (nuevo o existente).
        var op = await GetByBadgeCodeAsync(badgeCode, ct);
        return op ?? throw new InvalidOperationException(
            $"Bridge operator for user '{user.Username}' was not found after upsert.");
    }


    public async Task<Operator?> GetByBadgeCodeAsync(string badgeCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id               AS Id,
                   badge_code       AS BadgeCode,
                   employee_number  AS EmployeeNumber,
                   display_name     AS DisplayName,
                   is_active        AS IsActive,
                   is_admin         AS IsAdmin,
                   created_at_utc   AS CreatedAtUtc,
                   updated_at_utc   AS UpdatedAtUtc
            FROM operators_QA
            WHERE badge_code = @BadgeCode
            LIMIT 1
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<Operator>(sql, new { BadgeCode = badgeCode });
    }
}
