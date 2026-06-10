using FluentAssertions;
using Moq;
using QualityLock.Application.Configuration;
using QualityLock.Application.Exceptions;
using QualityLock.Application.Interfaces;
using QualityLock.Application.Services;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;

namespace QualityLock.Application.Tests;

public class BadgeValidationServiceTests
{
    private readonly Mock<ISystemUserRepository> _systemUserRepo = new();
    private readonly Mock<IOperatorRepository> _operatorRepo = new();
    private readonly Mock<IStationRepository> _stationRepo = new();
    private readonly Mock<IEventRepository> _eventRepo = new();
    private readonly AdminAccessOptions _adminAccess = new()
    {
        MinRoleLevel = 3, Roles = []
    };
    private readonly BadgeValidationService _sut;

    private static readonly Station ActiveStation = new()
    {
        Id = 1, StationCode = "ICT-01", StationName = "ICT Station 1",
        StationType = StationType.ICT, IsActive = true
    };

    private static readonly SystemUser ActiveUser = new()
    {
        Id = 7, Username = "Juan", PasswordHash = new string('a', 64),
        NombreCompleto = "Juan Lopez", Departamento = "Producción",
        Cargo = "Operador", Activo = true
    };

    private static readonly Operator BridgeOperator = new()
    {
        Id = 10, BadgeCode = "Juan", EmployeeNumber = "USR-7",
        DisplayName = "Juan Lopez", IsActive = true
    };

    public BadgeValidationServiceTests()
    {
        _sut = new BadgeValidationService(_systemUserRepo.Object, _operatorRepo.Object,
            _stationRepo.Object, _eventRepo.Object, _adminAccess);
    }

    [Fact]
    public async Task Validate_ActiveUser_ReturnsAllowed_AndBridgesOperator()
    {
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("ICT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("Juan", default)).ReturnsAsync(ActiveUser);
        _operatorRepo.Setup(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), false, default))
            .ReturnsAsync(BridgeOperator);
        _eventRepo.Setup(r => r.InsertAsync(It.IsAny<StationEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.ValidateAsync(new BadgeValidationRequest("ICT-01", "Juan", DateTime.UtcNow));

        result.Decision.Should().Be(ValidationDecision.Allowed);
        result.CanOperate.Should().BeTrue();
        result.DenyReason.Should().BeNull();
        result.DisplayName.Should().Be("Juan Lopez");
        _operatorRepo.Verify(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), false, default), Times.Once);
    }

    [Fact]
    public async Task Validate_UserWithAdminRole_BridgesAsAdmin()
    {
        var adminUser = new SystemUser
        {
            Id = 1, Username = "admin", PasswordHash = new string('a', 64),
            NombreCompleto = "Admin Sistemas", Departamento = "Sistemas",
            Cargo = "Administrador", Activo = true,
            // Rol superadmin (nivel 10) >= MinRoleLevel (3) → admin.
            Roles = [new Role { Id = 1, Nombre = "superadmin", Nivel = 10 }]
        };
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("ICT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("admin", default)).ReturnsAsync(adminUser);
        _operatorRepo.Setup(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), true, default))
            .ReturnsAsync(BridgeOperator);
        _eventRepo.Setup(r => r.InsertAsync(It.IsAny<StationEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.ValidateAsync(new BadgeValidationRequest("ICT-01", "admin", DateTime.UtcNow));

        result.Decision.Should().Be(ValidationDecision.Allowed);
        _operatorRepo.Verify(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), true, default), Times.Once);
    }

    [Fact]
    public async Task Validate_TecnicoQaRole_BridgesAsAdmin()
    {
        var qaUser = new SystemUser
        {
            Id = 38, Username = "1744", PasswordHash = new string('a', 64),
            NombreCompleto = "Tecnico QA", Departamento = "Calidad", Activo = true,
            // Tecnico QA tiene nivel 3 == MinRoleLevel → admin.
            Roles = [new Role { Id = 4816, Nombre = "Tecnico QA", Nivel = 3 }]
        };
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("ICT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("1744", default)).ReturnsAsync(qaUser);
        _operatorRepo.Setup(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), true, default))
            .ReturnsAsync(BridgeOperator);
        _eventRepo.Setup(r => r.InsertAsync(It.IsAny<StationEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.ValidateAsync(new BadgeValidationRequest("ICT-01", "1744", DateTime.UtcNow));

        result.Decision.Should().Be(ValidationDecision.Allowed);
        _operatorRepo.Verify(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), true, default), Times.Once);
    }

    [Fact]
    public async Task Validate_InactiveUser_ReturnsDenied()
    {
        var inactiveUser = new SystemUser
        {
            Id = 7, Username = "Juan", PasswordHash = new string('a', 64),
            NombreCompleto = "Juan Lopez", Activo = false
        };
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("ICT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("Juan", default)).ReturnsAsync(inactiveUser);
        _eventRepo.Setup(r => r.InsertAsync(It.IsAny<StationEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.ValidateAsync(new BadgeValidationRequest("ICT-01", "Juan", DateTime.UtcNow));

        result.Decision.Should().Be(ValidationDecision.Denied);
        _operatorRepo.Verify(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), It.IsAny<bool>(), default), Times.Never);
    }

    [Fact]
    public async Task Validate_UnknownUser_ReturnsDenied()
    {
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("ICT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("UNKNOWN", default)).ReturnsAsync((SystemUser?)null);
        _eventRepo.Setup(r => r.InsertAsync(It.IsAny<StationEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.ValidateAsync(new BadgeValidationRequest("ICT-01", "UNKNOWN", DateTime.UtcNow));

        result.Decision.Should().Be(ValidationDecision.Denied);
    }

    [Fact]
    public async Task Validate_UnknownStation_ThrowsNotFoundException()
    {
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("INVALID", It.IsAny<string>(), default)).ReturnsAsync((Station?)null);

        await _sut.Invoking(s => s.ValidateAsync(new BadgeValidationRequest("INVALID", "Juan", DateTime.UtcNow)))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Validate_AuditEventIsRecorded()
    {
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("ICT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("Juan", default)).ReturnsAsync(ActiveUser);
        _operatorRepo.Setup(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), It.IsAny<bool>(), default))
            .ReturnsAsync(BridgeOperator);
        _eventRepo.Setup(r => r.InsertAsync(It.IsAny<StationEvent>(), default)).Returns(Task.CompletedTask);

        await _sut.ValidateAsync(new BadgeValidationRequest("ICT-01", "Juan", DateTime.UtcNow));

        _eventRepo.Verify(r => r.InsertAsync(It.IsAny<StationEvent>(), default), Times.Once);
    }
}
