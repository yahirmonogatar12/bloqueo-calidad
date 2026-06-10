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

public class SessionServiceTests
{
    private readonly Mock<IStationRepository> _stationRepo = new();
    private readonly Mock<ISystemUserRepository> _systemUserRepo = new();
    private readonly Mock<IOperatorRepository> _operatorRepo = new();
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<IEventRepository> _eventRepo = new();
    private readonly AdminAccessOptions _adminAccess = new();
    private readonly SessionService _sut;

    private static readonly Station ActiveStation = new()
    {
        Id = 1, StationCode = "FCT-01", StationName = "FCT Station 1",
        StationType = StationType.FCT, IsActive = true
    };

    private static readonly SystemUser ActiveUser = new()
    {
        Id = 2, Username = "Yahir", PasswordHash = new string('a', 64),
        NombreCompleto = "Yahir Leon", Departamento = "Sistemas",
        Cargo = "Desarrollador", Activo = true
    };

    private static readonly Operator BridgeOperator = new()
    {
        Id = 20, BadgeCode = "Yahir", EmployeeNumber = "USR-2",
        DisplayName = "Yahir Leon", IsActive = true
    };

    public SessionServiceTests()
    {
        _sut = new SessionService(_stationRepo.Object, _systemUserRepo.Object,
            _operatorRepo.Object, _sessionRepo.Object, _eventRepo.Object, _adminAccess);
    }

    [Fact]
    public async Task StartSession_NoOpenSession_CreatesSession()
    {
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("FCT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _sessionRepo.Setup(r => r.GetOpenSessionByStationAsync(1, default)).ReturnsAsync((StationSession?)null);
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("Yahir", default)).ReturnsAsync(ActiveUser);
        _operatorRepo.Setup(r => r.EnsureBridgeOperatorAsync(
                It.IsAny<SystemUser>(), It.IsAny<bool>(), default))
            .ReturnsAsync(BridgeOperator);
        _sessionRepo.Setup(r => r.CreateWithUnlockEventAsync(
                It.IsAny<StationSession>(), It.IsAny<StationEvent>(), default))
            .Returns(Task.CompletedTask);

        var result = await _sut.StartAsync(new StartSessionRequest("FCT-01", "Yahir", DateTime.UtcNow, true, Guid.NewGuid().ToString()));

        result.SessionId.Should().NotBeEmpty();
        _sessionRepo.Verify(r => r.CreateWithUnlockEventAsync(
            It.IsAny<StationSession>(), It.IsAny<StationEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task StartSession_OpenSessionExists_AutoClosesAndCreatesNew()
    {
        // Una sesion abierta previa de la estacion estaba huerfana: se auto-cierra y se
        // abre la nueva (ya no se rechaza con 409).
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("FCT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _systemUserRepo.Setup(r => r.GetByUsernameAsync("Yahir", default)).ReturnsAsync(ActiveUser);
        _operatorRepo.Setup(r => r.EnsureBridgeOperatorAsync(It.IsAny<SystemUser>(), It.IsAny<bool>(), default))
            .ReturnsAsync(BridgeOperator);
        _sessionRepo.Setup(r => r.CreateWithUnlockEventAsync(
                It.IsAny<StationSession>(), It.IsAny<StationEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.StartAsync(
            new StartSessionRequest("FCT-01", "Yahir", DateTime.UtcNow, true, Guid.NewGuid().ToString()));

        result.SessionId.Should().NotBeEmpty();
        _sessionRepo.Verify(r => r.CloseOpenSessionsForStationAsync(1, default), Times.Once);
    }

    [Fact]
    public async Task StartSession_UnknownStation_ThrowsNotFound()
    {
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("INVALID", It.IsAny<string>(), default)).ReturnsAsync((Station?)null);

        await _sut.Invoking(s => s.StartAsync(
                new StartSessionRequest("INVALID", "BADGE002", DateTime.UtcNow, true, Guid.NewGuid().ToString())))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task EndSession_ValidSession_Closes()
    {
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("FCT-01", It.IsAny<string>(), default)).ReturnsAsync(ActiveStation);
        _sessionRepo.Setup(r => r.CloseAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<bool>(), default))
            .Returns(Task.CompletedTask);
        _eventRepo.Setup(r => r.InsertAsync(It.IsAny<StationEvent>(), default)).Returns(Task.CompletedTask);

        await _sut.Invoking(s => s.EndAsync(
                new EndSessionRequest(Guid.NewGuid(), "FCT-01", "AutoLock", DateTime.UtcNow, true)))
            .Should().NotThrowAsync();

        _sessionRepo.Verify(r => r.CloseAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>(), true, default), Times.Once);
    }
}
