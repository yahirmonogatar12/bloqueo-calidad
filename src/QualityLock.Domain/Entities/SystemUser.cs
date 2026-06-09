namespace QualityLock.Domain.Entities;

/// <summary>
/// Usuario del MES (tabla <c>usuarios_sistema</c>). Es la fuente de verdad para
/// "quién existe y está activo". El gafete trae el <see cref="Username"/>; el login
/// de admin valida <see cref="PasswordHash"/> (SHA-256 hex sin sal).
/// </summary>
public class SystemUser
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? NombreCompleto { get; init; }
    public string? Departamento { get; init; }
    public string? Cargo { get; init; }
    public bool Activo { get; init; }

    /// <summary>Roles asignados al usuario (vía <c>usuario_roles</c>). Puede estar vacío.</summary>
    public IReadOnlyList<Role> Roles { get; set; } = [];

    /// <summary>Nivel de rol más alto del usuario (0 si no tiene roles).</summary>
    public int MaxRoleLevel => Roles.Count == 0 ? 0 : Roles.Max(r => r.Nivel);

    /// <summary>Nombre del rol de mayor nivel (para mostrar). Null si no tiene roles.</summary>
    public string? TopRoleName => Roles.Count == 0
        ? null
        : Roles.OrderByDescending(r => r.Nivel).First().Nombre;

    /// <summary>Nombre a mostrar: nombre completo si existe, si no el username.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(NombreCompleto) ? Username : NombreCompleto!;
}
