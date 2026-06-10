using QualityLock.Application.Exceptions;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Shared.Constants;
using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Services;

public class HeartbeatService(
    IStationRepository stationRepo,
    IHeartbeatRepository heartbeatRepo,
    ISessionRepository sessionRepo) : IHeartbeatService
{
    public async Task RecordAsync(HeartbeatRequest request, CancellationToken ct = default)
    {
        var station = await stationRepo.GetByCodeAndLineAsync(request.StationCode, request.Line, ct)
            ?? throw new NotFoundException($"Station '{request.StationCode}' not found.");

        await heartbeatRepo.InsertAsync(new ClientHeartbeat
        {
            Id = Guid.NewGuid(),
            StationId = station.Id,
            SentAtUtc = request.SentAtUtc,
            ClientVersion = request.ClientVersion,
            IsSafeMode = request.IsSafeMode,
            LastActivityAtUtc = request.LastActivityAtUtc,
            DetailsJson = request.DetailsJson
        }, ct);

        // Oportunista: cierra sesiones huerfanas (clientes que murieron sin cerrar).
        // Es barato y corre con cada heartbeat de cualquier estacion.
        await sessionRepo.CloseStaleSessionsAsync(AppConstants.StaleSessionMinutes, ct);
    }
}
