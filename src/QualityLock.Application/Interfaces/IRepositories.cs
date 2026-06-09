using QualityLock.Domain.Entities;

namespace QualityLock.Application.Interfaces;

public interface IOperatorRepository
{
    Task<Operator?> GetByBadgeCodeAsync(string badgeCode, CancellationToken ct = default);

    /// <summary>
    /// Garantiza que exista una fila en <c>operators_QA</c> que represente a un usuario
    /// de <c>usuarios_sistema</c>, y devuelve su id. Necesario porque las sesiones y
    /// eventos tienen FK a <c>operators_QA</c>. Idempotente: actualiza nombre/estado
    /// si el operador puente ya existe.
    /// </summary>
    Task<Operator> EnsureBridgeOperatorAsync(SystemUser user, bool isAdmin, CancellationToken ct = default);
}

public interface ISystemUserRepository
{
    /// <summary>Busca un usuario por username (case-insensitive). Null si no existe.</summary>
    Task<SystemUser?> GetByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Todos los usuarios activos con sus roles, para poblar la caché offline de la
    /// estación. Permite que cualquier usuario válido desbloquee sin conexión.
    /// </summary>
    Task<IReadOnlyList<SystemUser>> GetActiveUsersAsync(CancellationToken ct = default);
}

public interface IStationRepository
{
    Task<Station?> GetByCodeAsync(string stationCode, CancellationToken ct = default);
    Task<int> UpsertAsync(Station station, CancellationToken ct = default);
}

public interface ISessionRepository
{
    Task<Guid> CreateAsync(StationSession session, CancellationToken ct = default);

    /// <summary>
    /// Inserts the session and its UnlockGranted event in a single transaction.
    /// The unique "one open session per station" DB constraint is the source of
    /// truth: a concurrent duplicate surfaces as a ConflictException.
    /// </summary>
    Task CreateWithUnlockEventAsync(StationSession session, StationEvent unlockEvent, CancellationToken ct = default);

    Task<StationSession?> GetOpenSessionByStationAsync(int stationId, CancellationToken ct = default);
    Task CloseAsync(Guid sessionId, DateTime endedAtUtc, string status, bool endedOnline, CancellationToken ct = default);
}

public interface IEventRepository
{
    Task InsertAsync(StationEvent evt, CancellationToken ct = default);
    Task InsertBatchAsync(IEnumerable<StationEvent> events, CancellationToken ct = default);
}

public interface IAdminOverrideRepository
{
    Task<Guid> CreateAsync(AdminOverride adminOverride, CancellationToken ct = default);
}

public interface IHeartbeatRepository
{
    Task InsertAsync(ClientHeartbeat heartbeat, CancellationToken ct = default);
}
