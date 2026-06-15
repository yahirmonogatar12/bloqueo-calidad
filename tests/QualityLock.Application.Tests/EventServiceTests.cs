using FluentAssertions;
using Moq;
using QualityLock.Application.Configuration;
using QualityLock.Application.Interfaces;
using QualityLock.Application.Services;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;

namespace QualityLock.Application.Tests;

public class EventServiceTests
{
    private readonly Mock<IStationRepository> _stationRepo = new();
    private readonly Mock<ISystemUserRepository> _systemUserRepo = new();
    private readonly Mock<IOperatorRepository> _operatorRepo = new();
    private readonly Mock<IEventRepository> _eventRepo = new();
    private readonly EventService _sut;

    private static readonly Station ActiveStation = new()
    {
        Id = 1,
        StationCode = "ICT-01",
        StationName = "ICT Station 1",
        StationType = StationType.ICT,
        IsActive = true
    };

    public EventServiceTests()
    {
        _stationRepo.Setup(r => r.GetByCodeAndLineAsync("ICT-01", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveStation);

        _sut = new EventService(
            _stationRepo.Object,
            _systemUserRepo.Object,
            _operatorRepo.Object,
            _eventRepo.Object,
            new AdminAccessOptions());
    }

    [Fact]
    public async Task RecordBatch_OfflineClientEvent_DropsSessionId()
    {
        var offlineSessionId = Guid.NewGuid();
        List<StationEvent> captured = [];
        _eventRepo.Setup(r => r.InsertBatchAsync(It.IsAny<IEnumerable<StationEvent>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<StationEvent>, CancellationToken>((events, _) => captured = events.ToList())
            .Returns(Task.CompletedTask);

        await _sut.RecordBatchAsync([
            new StationEventRequest(
                "ICT-01",
                BadgeCode: null,
                offlineSessionId,
                StationEventType.AutoLock,
                DateTime.UtcNow,
                DetailsJson: null,
                Source: "Client-Offline",
                CorrelationId: Guid.NewGuid().ToString(),
                Line: "M1")
        ]);

        captured.Should().ContainSingle();
        captured[0].SessionId.Should().BeNull();
    }

    [Fact]
    public async Task RecordBatch_OnlineEvent_PreservesSessionId()
    {
        var sessionId = Guid.NewGuid();
        List<StationEvent> captured = [];
        _eventRepo.Setup(r => r.InsertBatchAsync(It.IsAny<IEnumerable<StationEvent>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<StationEvent>, CancellationToken>((events, _) => captured = events.ToList())
            .Returns(Task.CompletedTask);

        await _sut.RecordBatchAsync([
            new StationEventRequest(
                "ICT-01",
                BadgeCode: null,
                sessionId,
                StationEventType.AutoLock,
                DateTime.UtcNow,
                DetailsJson: null,
                Source: "Client",
                CorrelationId: Guid.NewGuid().ToString(),
                Line: "M1")
        ]);

        captured.Should().ContainSingle();
        captured[0].SessionId.Should().Be(sessionId);
    }
}
