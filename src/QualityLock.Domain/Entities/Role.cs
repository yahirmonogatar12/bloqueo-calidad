namespace QualityLock.Domain.Entities;

/// <summary>
/// Rol del MES (tabla <c>roles</c>), asignado a usuarios vía <c>usuario_roles</c>.
/// El <see cref="Nivel"/> es jerárquico (superadmin=10 … invitado=1) y es lo que
/// QualityLock usa para decidir privilegios de administrador.
/// </summary>
public class Role
{
    public int Id { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public int Nivel { get; init; }
}
