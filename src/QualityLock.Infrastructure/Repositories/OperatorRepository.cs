using Dapper;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

public class OperatorRepository(IDbConnectionFactory factory) : IOperatorRepository
{
    /// <summary>
    /// Crea o actualiza una fila puente en <c>operators_QA</c> que representa a un usuario
    /// de <c>usuarios_sistema</c>: <c>badge_code = username</c> y
    /// <c>employee_number = "USR-{id}"</c> (ambas UNIQUE). Idempotente.
    /// La llave estable es <c>employee_number</c>: si al usuario le cambian el username
    /// ("Eduardo" -> "1746"), la misma fila toma el nuevo badge_code y conserva su id, asi
    /// sus sesiones y eventos siguen ligados.
    /// </summary>
    public async Task<Operator> EnsureBridgeOperatorAsync(SystemUser user, bool isAdmin, CancellationToken ct = default)
    {
        const string upsertSql = """
            INSERT INTO operators_QA (badge_code, employee_number, display_name, is_active, is_admin)
            VALUES (@BadgeCode, @EmployeeNumber, @DisplayName, @IsActive, @IsAdmin)
            ON DUPLICATE KEY UPDATE
                badge_code   = VALUES(badge_code),
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

        // Re-leemos para obtener el id auto-incremental (nuevo o existente). Primero por
        // employee_number (la llave estable); por badge_code si el upsert choco con una fila
        // vieja que ya tenia ese badge pero otro employee_number.
        var op = await conn.QueryFirstOrDefaultAsync<Operator>(
                     SelectSql + " WHERE employee_number = @EmployeeNumber LIMIT 1",
                     new { EmployeeNumber = employeeNumber })
                 ?? await conn.QueryFirstOrDefaultAsync<Operator>(
                     SelectSql + " WHERE badge_code = @BadgeCode LIMIT 1",
                     new { BadgeCode = badgeCode });
        return op ?? throw new InvalidOperationException(
            $"Bridge operator for user '{user.Username}' was not found after upsert.");
    }

    private const string SelectSql = """
        SELECT id               AS Id,
               badge_code       AS BadgeCode,
               employee_number  AS EmployeeNumber,
               display_name     AS DisplayName,
               is_active        AS IsActive,
               is_admin         AS IsAdmin,
               created_at_utc   AS CreatedAtUtc,
               updated_at_utc   AS UpdatedAtUtc
        FROM operators_QA
        """;


    public async Task<Operator?> GetByBadgeCodeAsync(string badgeCode, CancellationToken ct = default)
    {
        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<Operator>(
            SelectSql + " WHERE badge_code = @BadgeCode LIMIT 1", new { BadgeCode = badgeCode });
    }
}
