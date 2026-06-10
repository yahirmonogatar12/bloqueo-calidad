using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Services;

public class StationRegistrationService(IStationRepository stationRepo) : IStationRegistrationService
{
    public async Task<RegisterStationResponse> RegisterAsync(RegisterStationRequest request, CancellationToken ct = default)
    {
        // La estacion se identifica por codigo + linea (HostName). El mismo codigo puede
        // existir en lineas distintas (M1, M2) como estaciones separadas.
        var existing = await stationRepo.GetByCodeAndLineAsync(request.StationCode, request.HostName, ct);
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
