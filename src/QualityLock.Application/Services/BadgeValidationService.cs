using System.Text.Json;
using QualityLock.Application.Configuration;
using QualityLock.Application.Exceptions;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;

namespace QualityLock.Application.Services;

/// <summary>
/// Valida un desbloqueo. El gafete trae el <c>username</c> de <c>usuarios_sistema</c>
/// (fuente de verdad). Si el usuario existe y está activo se concede el acceso; se
/// crea/actualiza una fila puente en <c>operators_QA</c> para que sesiones y eventos
/// (con FK a operators) sean válidos.
/// </summary>
public class BadgeValidationService(
    ISystemUserRepository systemUserRepo,
    IOperatorRepository operatorRepo,
    IStationRepository stationRepo,
    IEventRepository eventRepo,
    AdminAccessOptions adminAccess) : IBadgeValidationService
{
    private readonly AdminAccessOptions _adminAccess = adminAccess;

    public async Task<BadgeValidationResponse> ValidateAsync(BadgeValidationRequest request, CancellationToken ct = default)
    {
        var station = await stationRepo.GetByCodeAndLineAsync(request.StationCode, request.Line, ct)
            ?? throw new NotFoundException($"Station '{request.StationCode}' not found.");

        if (!station.IsActive)
            throw new ValidationException($"Station '{request.StationCode}' is not active.");

        var stationSnapshot = new StationSnapshot(station.StationCode, station.StationName, station.StationType, station.IsActive);

        // El "badge code" escaneado es el username del MES.
        var user = await systemUserRepo.GetByUsernameAsync(request.BadgeCode, ct);

        if (user is null || !user.Activo)
        {
            await eventRepo.InsertAsync(new Domain.Entities.StationEvent
            {
                Id = Guid.NewGuid(),
                StationId = station.Id,
                EventType = StationEventType.UnlockDenied,
                EventAtUtc = request.ClientUtc,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    username = request.BadgeCode,
                    reason = "user_not_found_or_inactive"
                }),
                Source = "API",
                CorrelationId = Guid.NewGuid().ToString()
            }, ct);

            return new BadgeValidationResponse(
                request.BadgeCode,
                string.Empty,
                string.Empty,
                ValidationDecision.Denied,
                "Usuario no encontrado o inactivo.",
                false,
                stationSnapshot);
        }

        var isAdmin = _adminAccess.IsAdmin(user.Roles);

        // Puente a operators_QA para mantener la integridad referencial de sesiones/eventos.
        var op = await operatorRepo.EnsureBridgeOperatorAsync(user, isAdmin, ct);

        await eventRepo.InsertAsync(new Domain.Entities.StationEvent
        {
            Id = Guid.NewGuid(),
            StationId = station.Id,
            OperatorId = op.Id,
            EventType = StationEventType.UnlockGranted,
            EventAtUtc = request.ClientUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                username = user.Username,
                departamento = user.Departamento,
                cargo = user.Cargo,
                roleLevel = user.MaxRoleLevel,
                isAdmin
            }),
            Source = "API",
            CorrelationId = Guid.NewGuid().ToString()
        }, ct);

        return new BadgeValidationResponse(
            user.Username,
            $"USR-{user.Id}",
            user.DisplayName,
            ValidationDecision.Allowed,
            null,
            true,
            stationSnapshot,
            user.TopRoleName);
    }
}
