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
    /// <summary>
    /// Busca una estacion por codigo + linea (host_name). La combinacion es unica, asi
    /// que el mismo codigo (ej. ICT-01) puede existir en lineas distintas (M1, M2).
    /// Si <paramref name="line"/> viene vacio, busca solo por codigo (compatibilidad).
    /// </summary>
    Task<Station?> GetByCodeAndLineAsync(string stationCode, string line, CancellationToken ct = default);
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

    /// <summary>
    /// Cierra ("AutoClosed") las sesiones que quedaron abiertas porque el cliente murio
    /// sin cerrarlas: aquellas cuya estacion no envia heartbeat desde hace mas de
    /// <paramref name="staleMinutes"/>. La hora de cierre es el ultimo heartbeat conocido
    /// (o el inicio de la sesion si no hubo ninguno posterior). Devuelve cuantas cerro.
    /// </summary>
    Task<int> CloseStaleSessionsAsync(int staleMinutes, CancellationToken ct = default);

    /// <summary>
    /// Cierra ("AutoClosed") cualquier sesion abierta de la estacion indicada, usando la
    /// hora del ultimo heartbeat (o el inicio si no hubo). Se llama al abrir una sesion
    /// nueva: una estacion no puede tener dos sesiones vivas, asi que la anterior estaba
    /// huerfana (cliente reiniciado). Devuelve cuantas cerro.
    /// </summary>
    Task<int> CloseOpenSessionsForStationAsync(int stationId, CancellationToken ct = default);
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
