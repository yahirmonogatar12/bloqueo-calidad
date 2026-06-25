using QualityLock.Application.Configuration;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Services;

public class EventService(
    IStationRepository stationRepo,
    ISystemUserRepository systemUserRepo,
    IOperatorRepository operatorRepo,
    IEventRepository eventRepo,
    AdminAccessOptions adminAccess) : IEventService
{
    private const string OfflineClientSource = "Client-Offline";

    public async Task RecordAsync(StationEventRequest request, CancellationToken ct = default)
    {
        await RecordBatchAsync([request], ct);
    }

    public async Task RecordBatchAsync(IEnumerable<StationEventRequest> requests, CancellationToken ct = default)
    {
        var events = new List<StationEvent>();

        foreach (var req in requests)
        {
            // Auditoría best-effort: un evento de una estación inexistente (ej. estación
            // de prueba mal configurada) NO debe tumbar el batch entero y dejar la cola
            // del cliente atascada para siempre. Se omite y se sigue con los demás.
            var station = await stationRepo.GetByCodeAndLineAsync(req.StationCode, req.Line, ct);
            if (station is null)
                continue;

            // El BadgeCode de un evento (offline u online) es el username del MES. Lo
            // puenteamos a operators_QA para atribuir el evento; si el usuario ya no
            // existe, el operator_id queda null (la columna lo permite).
            int? operatorId = await ResolveOperatorIdAsync(req.BadgeCode, ct);

            events.Add(new StationEvent
            {
                Id = Guid.NewGuid(),
                StationId = station.Id,
                OperatorId = operatorId,
                SessionId = IsOfflineClientEvent(req) ? null : req.SessionId,
                EventType = req.EventType,
                EventAtUtc = req.EventAtUtc,
                DetailsJson = req.DetailsJson,
                Source = req.Source,
                CorrelationId = req.CorrelationId
            });
        }

        await eventRepo.InsertBatchAsync(events, ct);
    }

    private async Task<int?> ResolveOperatorIdAsync(string? badgeCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(badgeCode))
            return null;

        var user = await systemUserRepo.GetByUsernameAsync(badgeCode, ct);
        if (user is not null)
        {
            var op = await operatorRepo.EnsureBridgeOperatorAsync(user, adminAccess.IsAdmin(user.Roles), ct);
            return op.Id;
        }

        // Respaldo: si el badge ya corresponde a un operador puente existente, úsalo.
        var existing = await operatorRepo.GetByBadgeCodeAsync(badgeCode, ct);
        return existing?.Id;
    }

    private static bool IsOfflineClientEvent(StationEventRequest request)
        => string.Equals(request.Source, OfflineClientSource, StringComparison.OrdinalIgnoreCase);
}
