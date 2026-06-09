using QualityLock.Application.Configuration;
using QualityLock.Application.Exceptions;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Services;

public class StationBootstrapService(
    IStationRepository stationRepo,
    ISystemUserRepository systemUserRepo,
    AdminAccessOptions adminAccess) : IStationBootstrapService
{
    public async Task<StationBootstrapResponse> GetBootstrapAsync(string stationCode, CancellationToken ct = default)
    {
        var station = await stationRepo.GetByCodeAsync(stationCode, ct)
            ?? throw new NotFoundException($"Station '{stationCode}' not found.");

        if (!station.IsActive)
            throw new ValidationException($"Station '{stationCode}' is not active.");

        // La caché offline se llena con TODOS los usuarios activos de usuarios_sistema,
        // de modo que cualquier usuario válido pueda desbloquear sin conexión. El "badge"
        // cacheado es el username; isAdmin se deriva de los roles del usuario.
        var users = await systemUserRepo.GetActiveUsersAsync(ct);

        var cachedOperators = users.Select(u => new CachedOperatorDto(
            u.Username,
            $"USR-{u.Id}",
            u.DisplayName,
            adminAccess.IsAdmin(u.Roles),
            u.TopRoleName)).ToList();

        var stationSnapshot = new StationSnapshot(
            station.StationCode,
            station.StationName,
            station.StationType,
            station.IsActive);

        return new StationBootstrapResponse(stationSnapshot, cachedOperators, DateTime.UtcNow);
    }
}
