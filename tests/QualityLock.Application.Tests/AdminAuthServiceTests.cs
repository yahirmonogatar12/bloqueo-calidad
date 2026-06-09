using FluentAssertions;
using Moq;
using QualityLock.Application.Configuration;
using QualityLock.Application.Interfaces;
using QualityLock.Application.Services;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Security;

namespace QualityLock.Application.Tests;

public class AdminAuthServiceTests
{
    private readonly Mock<ISystemUserRepository> _systemUserRepo = new();
    private readonly AdminAccessOptions _adminAccess = new()
    {
        MinRoleLevel = 3, Roles = []
    };
    private readonly AdminAuthService _sut;

    // SHA-256("123") — vector real tomado de usuarios_sistema (usuario "Prueba").
    private const string HashOf123 = "a665a45920422f9d417e4867efdc4fb8a04a1f3fff1fa07e998e86f7f7a27ae3";

    private static readonly Role[] AdminRole = [new Role { Id = 1, Nombre = "superadmin", Nivel = 10 }];
    private static readonly Role[] OperatorRole = [new Role { Id = 6, Nombre = "operador_produccion", Nivel = 4 }];
    private static readonly Role[] LowRole = [new Role { Id = 4809, Nombre = "invitado", Nivel = 1 }];

    public AdminAuthServiceTests()
        => _sut = new AdminAuthService(_systemUserRepo.Object, _adminAccess);

    private static SystemUser User(Role[] roles, bool activo = true) => new()
    {
        Id = 1, Username = "admin", PasswordHash = HashOf123,
        NombreCompleto = "Admin", Departamento = "Sistemas", Cargo = "Administrador",
        Activo = activo, Roles = roles
    };

    [Fact]
    public async Task Login_SupervisorRole_IsAdminButCannotUnlockManually()
    {
        // supervisor_almacen (nivel 8) es admin por nivel, PERO no esta en
        // ManualUnlockRoles (superadmin/admin/Tecnico QA) -> no puede desbloquear a mano.
        var supervisor = new[] { new Role { Id = 3, Nombre = "supervisor_almacen", Nivel = 8 } };
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("admin", default)).ReturnsAsync(User(supervisor));

        var result = await _sut.LoginAsync(new AdminLoginRequest("admin", "123"));

        result.IsAdmin.Should().BeTrue();
        result.CanUnlockManually.Should().BeFalse();
    }

    [Fact]
    public async Task Login_TecnicoQaRole_CanUnlockManually()
    {
        var qa = new[] { new Role { Id = 4816, Nombre = "Tecnico QA", Nivel = 3 } };
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("admin", default)).ReturnsAsync(User(qa));

        var result = await _sut.LoginAsync(new AdminLoginRequest("admin", "123"));

        result.CanUnlockManually.Should().BeTrue();
    }

    [Fact]
    public async Task Login_ValidPasswordAdminRole_AuthenticatedAndAdmin()
    {
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("admin", default))
            .ReturnsAsync(User(AdminRole));

        var result = await _sut.LoginAsync(new AdminLoginRequest("admin", "123"));

        result.Authenticated.Should().BeTrue();
        result.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Login_ValidPasswordLowRole_AuthenticatedButNotAdmin()
    {
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("admin", default))
            .ReturnsAsync(User(LowRole));

        var result = await _sut.LoginAsync(new AdminLoginRequest("admin", "123"));

        result.Authenticated.Should().BeTrue();
        result.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Login_RoleLevelMeetsThreshold_IsAdmin()
    {
        // operador_produccion nivel 4 >= MinRoleLevel 3 → admin con el umbral por defecto.
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("admin", default))
            .ReturnsAsync(User(OperatorRole));

        var result = await _sut.LoginAsync(new AdminLoginRequest("admin", "123"));

        result.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Login_WrongPassword_NotAuthenticated()
    {
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("admin", default))
            .ReturnsAsync(User(AdminRole));

        var result = await _sut.LoginAsync(new AdminLoginRequest("admin", "wrong-password"));

        result.Authenticated.Should().BeFalse();
        result.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Login_InactiveUser_NotAuthenticated()
    {
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("admin", default))
            .ReturnsAsync(User(AdminRole, activo: false));

        var result = await _sut.LoginAsync(new AdminLoginRequest("admin", "123"));

        result.Authenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Login_UnknownUser_NotAuthenticated()
    {
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("ghost", default))
            .ReturnsAsync((SystemUser?)null);

        var result = await _sut.LoginAsync(new AdminLoginRequest("ghost", "123"));

        result.Authenticated.Should().BeFalse();
    }

    [Fact]
    public void Sha256Password_VerifiesKnownVector()
    {
        Sha256Password.Verify("123", HashOf123).Should().BeTrue();
        Sha256Password.Verify("123", HashOf123.ToUpperInvariant()).Should().BeTrue();
        Sha256Password.Verify("124", HashOf123).Should().BeFalse();
        Sha256Password.Verify("123", null).Should().BeFalse();
    }
}
