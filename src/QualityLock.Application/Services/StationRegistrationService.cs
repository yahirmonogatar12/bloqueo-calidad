using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Services;

public class StationRegistrationService(IStationRepository stationRepo) : IStationRegistrationService
{
    public async Task<RegisterStationResponse> RegisterAsync(RegisterStationRequest request, CancellationToken ct = default)
    {
        var existing = await stationRepo.GetByCodeAsync(request.StationCode, ct);
        var created = existing is null;

        var station = new Station
        {
            Id = existing?.Id ?? 0,
            StationCode = request.StationCode,
            StationName = request.StationName,
            StationType = request.StationType,
            HostName = request.HostName,
            IsActive = request.IsActive
        };

        var id = await stationRepo.UpsertAsync(station, ct);

        return new RegisterStationResponse(
            id,
            request.StationCode,
            created,
            created ? "Estación registrada correctamente." : "Estación actualizada correctamente.");
    }
}
