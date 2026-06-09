using QualityLock.Application.Exceptions;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Services;

public class HeartbeatService(
    IStationRepository stationRepo,
    IHeartbeatRepository heartbeatRepo) : IHeartbeatService
{
    public async Task RecordAsync(HeartbeatRequest request, CancellationToken ct = default)
    {
        var station = await stationRepo.GetByCodeAsync(request.StationCode, ct)
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
    }
}
