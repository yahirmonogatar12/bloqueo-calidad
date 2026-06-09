using QualityLock.Application.Configuration;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Security;

namespace QualityLock.Application.Services;

/// <summary>
/// Autentica administradores contra <c>usuarios_sistema</c> (username + password SHA-256)
/// y aplica el gating por departamento/cargo. Reemplaza el PIN <c>admin1234</c>.
/// </summary>
public class AdminAuthService(
    ISystemUserRepository systemUserRepo,
    AdminAccessOptions adminAccess) : IAdminAuthService
{
    private readonly AdminAccessOptions _adminAccess = adminAccess;

    public async Task<AdminLoginResponse> LoginAsync(AdminLoginRequest request, CancellationToken ct = default)
    {
        var denied = new AdminLoginResponse(
            Authenticated: false, IsAdmin: false, Username: request.Username ?? string.Empty,
            DisplayName: string.Empty, Departamento: null, Cargo: null,
            Message: "Usuario o contraseña inválidos.");

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return denied;

        var user = await systemUserRepo.GetByUsernameAsync(request.Username, ct);
        if (user is null || !user.Activo)
            return denied;

        if (!Sha256Password.Verify(request.Password, user.PasswordHash))
            return denied;

        var isAdmin = _adminAccess.IsAdmin(user.Roles);
        var canUnlockManually = _adminAccess.CanUnlockManually(user.Roles);

        return new AdminLoginResponse(
            Authenticated: true,
            IsAdmin: isAdmin,
            Username: user.Username,
            DisplayName: user.DisplayName,
            Departamento: user.Departamento,
            Cargo: user.Cargo,
            Message: isAdmin ? null : "El usuario no tiene privilegios de administrador.",
            Role: user.TopRoleName,
            CanUnlockManually: canUnlockManually);
    }
}
