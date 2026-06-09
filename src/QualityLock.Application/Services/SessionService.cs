using System.Text.Json;
using QualityLock.Application.Configuration;
using QualityLock.Application.Exceptions;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;

namespace QualityLock.Application.Services;

public class SessionService(
    IStationRepository stationRepo,
    ISystemUserRepository systemUserRepo,
    IOperatorRepository operatorRepo,
    ISessionRepository sessionRepo,
    IEventRepository eventRepo,
    AdminAccessOptions adminAccess) : ISessionService
{
    private readonly AdminAccessOptions _adminAccess = adminAccess;

    public async Task<StartSessionResponse> StartAsync(StartSessionRequest request, CancellationToken ct = default)
    {
        var station = await stationRepo.GetByCodeAsync(request.StationCode, ct)
            ?? throw new NotFoundException($"Station '{request.StationCode}' not found.");

        if (!station.IsActive)
            throw new ValidationException($"Station '{request.StationCode}' is not active.");

        var existingSession = await sessionRepo.GetOpenSessionByStationAsync(station.Id, ct);
        if (existingSession is not null)
            throw new ConflictException($"Station '{request.StationCode}' already has an open session.");

        // El "badge code" es el username del MES (usuarios_sistema, fuente de verdad).
        var user = await systemUserRepo.GetByUsernameAsync(request.BadgeCode, ct)
            ?? throw new NotFoundException($"User '{request.BadgeCode}' not found.");

        if (!user.Activo)
            throw new ValidationException($"User '{request.BadgeCode}' is not active.");

        // Puente a operators_QA para satisfacer la FK de sesiones/eventos.
        var isAdmin = _adminAccess.IsAdmin(user.Roles);
        var op = await operatorRepo.EnsureBridgeOperatorAsync(user, isAdmin, ct);

        var session = new StationSession
        {
            Id = Guid.NewGuid(),
            StationId = station.Id,
            OperatorId = op.Id,
            StartedAtUtc = request.ClientUtc,
            Status = SessionStatus.Open,
            StartedOnline = request.IsOnline,
            CorrelationId = request.CorrelationId
        };

        var unlockEvent = new StationEvent
        {
            Id = Guid.NewGuid(),
            StationId = station.Id,
            OperatorId = op.Id,
            SessionId = session.Id,
            EventType = StationEventType.UnlockGranted,
            EventAtUtc = request.ClientUtc,
            Source = request.IsOnline ? "API" : "Client-Offline",
            CorrelationId = request.CorrelationId
        };

        // Single transaction: the session and its unlock event commit together, and
        // the unique open-session DB constraint rejects any concurrent duplicate
        // (surfaced as ConflictException). The check above is a fast-path for the
        // common case; the constraint is the real guarantee against the race.
        await sessionRepo.CreateWithUnlockEventAsync(session, unlockEvent, ct);

        return new StartSessionResponse(session.Id, session.StartedAtUtc);
    }

    public async Task EndAsync(EndSessionRequest request, CancellationToken ct = default)
    {
        var station = await stationRepo.GetByCodeAsync(request.StationCode, ct)
            ?? throw new NotFoundException($"Station '{request.StationCode}' not found.");

        await sessionRepo.CloseAsync(
            request.SessionId, request.ClientUtc, nameof(SessionStatus.Closed), request.IsOnline, ct);

        await eventRepo.InsertAsync(new StationEvent
        {
            Id = Guid.NewGuid(),
            StationId = station.Id,
            SessionId = request.SessionId,
            EventType = StationEventType.AutoLock,
            EventAtUtc = request.ClientUtc,
            DetailsJson = JsonSerializer.Serialize(new { reason = request.Reason }),
            Source = request.IsOnline ? "API" : "Client-Offline",
            CorrelationId = Guid.NewGuid().ToString()
        }, ct);
    }
}
