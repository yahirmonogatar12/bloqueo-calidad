using QualityLock.Domain.Entities;

namespace QualityLock.Application.Configuration;

/// <summary>
/// Define qué usuarios de <c>usuarios_sistema</c> cuentan como administradores
/// (pueden abrir el panel, detener el servicio y registrar overrides). El criterio
/// se basa en los <b>roles</b> del usuario (tabla <c>roles</c>, vía <c>usuario_roles</c>):
/// es admin si tiene algún rol con <see cref="MinRoleLevel"/> o superior, o algún rol
/// cuyo nombre esté en <see cref="Roles"/>. Sección de configuración: "Admin".
/// </summary>
public class AdminAccessOptions
{
    /// <summary>
    /// Nivel mínimo de rol para ser admin (jerarquía de <c>roles.nivel</c>:
    /// superadmin=10, admin=9, supervisores=8..4, calidad/Tecnico QA=3, …).
    /// Por defecto 3, que incluye a Calidad y Tecnico QA.
    /// </summary>
    public int MinRoleLevel { get; set; } = 3;

    /// <summary>
    /// Nombres de rol que siempre conceden admin, sin importar el nivel
    /// (ej. "superadmin", "Tecnico QA"). Comparación sin distinguir mayúsculas.
    /// </summary>
    public string[] Roles { get; set; } = [];

    /// <summary>
    /// Roles (por nombre exacto) que pueden desbloquear MANUALMENTE (tecleando + contraseña)
    /// cuando la estación exige escáner. Es una lista cerrada, NO por nivel. Por defecto
    /// superadmin, admin y Tecnico QA. Comparación sin distinguir mayúsculas.
    /// </summary>
    public string[] ManualUnlockRoles { get; set; } = ["superadmin", "admin", "Tecnico QA"];

    /// <summary>True si alguno de los roles del usuario alcanza el nivel mínimo o está en la lista.</summary>
    public bool IsAdmin(IReadOnlyList<Role> userRoles)
    {
        if (userRoles is null || userRoles.Count == 0)
            return false;

        foreach (var role in userRoles)
        {
            if (role.Nivel >= MinRoleLevel)
                return true;

            if (NameMatches(Roles, role.Nombre))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True si alguno de los roles del usuario está en <see cref="ManualUnlockRoles"/>
    /// (lista cerrada por nombre, ej. superadmin/admin/Tecnico QA).
    /// </summary>
    public bool CanUnlockManually(IReadOnlyList<Role> userRoles)
    {
        if (userRoles is null || userRoles.Count == 0)
            return false;

        foreach (var role in userRoles)
            if (NameMatches(ManualUnlockRoles, role.Nombre))
                return true;

        return false;
    }

    private static bool NameMatches(string[] allowed, string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName) || allowed.Length == 0)
            return false;

        foreach (var entry in allowed)
        {
            if (string.Equals(entry?.Trim(), roleName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
