using Dapper;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Infrastructure.Database;

namespace QualityLock.Infrastructure.Repositories;

/// <summary>
/// Acceso a la tabla <c>usuarios_sistema</c> del MES (compartida con el resto de los
/// sistemas) y a los roles del usuario vía <c>usuario_roles</c> → <c>roles</c>.
/// Solo lectura: QualityLock no crea ni modifica usuarios ni roles aquí.
/// </summary>
public class SystemUserRepository(IDbConnectionFactory factory) : ISystemUserRepository
{
    public async Task<SystemUser?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        // username es TEXT en la tabla; comparamos sin distinguir mayúsculas/minúsculas.
        const string userSql = """
            SELECT id              AS Id,
                   username        AS Username,
                   password_hash   AS PasswordHash,
                   email           AS Email,
                   nombre_completo AS NombreCompleto,
                   departamento    AS Departamento,
                   cargo           AS Cargo,
                   activo          AS Activo
            FROM usuarios_sistema
            WHERE LOWER(username) = LOWER(@Username)
            LIMIT 1
            """;

        const string rolesSql = """
            SELECT r.id     AS Id,
                   r.nombre AS Nombre,
                   r.nivel  AS Nivel
            FROM usuario_roles ur
            INNER JOIN roles r ON r.id = ur.rol_id
            WHERE ur.usuario_id = @UserId
              AND r.activo = 1
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);

        var user = await conn.QueryFirstOrDefaultAsync<SystemUser>(userSql, new { Username = username?.Trim() });
        if (user is null)
            return null;

        var roles = await conn.QueryAsync<Role>(rolesSql, new { UserId = user.Id });
        user.Roles = roles.ToList();
        return user;
    }

    public async Task<IReadOnlyList<SystemUser>> GetActiveUsersAsync(CancellationToken ct = default)
    {
        const string usersSql = """
            SELECT id              AS Id,
                   username        AS Username,
                   password_hash   AS PasswordHash,
                   email           AS Email,
                   nombre_completo AS NombreCompleto,
                   departamento    AS Departamento,
                   cargo           AS Cargo,
                   activo          AS Activo
            FROM usuarios_sistema
            WHERE activo = 1
            """;

        // Todos los pares (usuario, rol) de usuarios activos en una sola consulta.
        const string rolesSql = """
            SELECT ur.usuario_id AS UsuarioId,
                   r.id          AS Id,
                   r.nombre      AS Nombre,
                   r.nivel       AS Nivel
            FROM usuario_roles ur
            INNER JOIN roles r            ON r.id = ur.rol_id
            INNER JOIN usuarios_sistema u ON u.id = ur.usuario_id
            WHERE r.activo = 1
              AND u.activo = 1
            """;

        using var conn = factory.CreateConnection();
        await conn.OpenAsync(ct);

        var users = (await conn.QueryAsync<SystemUser>(usersSql)).ToList();
        var roleRows = await conn.QueryAsync<(int UsuarioId, int Id, string Nombre, int Nivel)>(rolesSql);

        var rolesByUser = roleRows
            .GroupBy(x => x.UsuarioId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Role>)g
                    .Select(x => new Role { Id = x.Id, Nombre = x.Nombre, Nivel = x.Nivel })
                    .ToList());

        foreach (var user in users)
        {
            if (rolesByUser.TryGetValue(user.Id, out var userRoles))
                user.Roles = userRoles;
        }

        return users;
    }
}
