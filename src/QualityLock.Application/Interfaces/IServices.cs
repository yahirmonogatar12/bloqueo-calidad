using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Interfaces;

public interface IBadgeValidationService
{
    Task<BadgeValidationResponse> ValidateAsync(BadgeValidationRequest request, CancellationToken ct = default);
}

public interface ISessionService
{
    Task<StartSessionResponse> StartAsync(StartSessionRequest request, CancellationToken ct = default);
    Task EndAsync(EndSessionRequest request, CancellationToken ct = default);
}

public interface IEventService
{
    Task RecordAsync(StationEventRequest request, CancellationToken ct = default);
    Task RecordBatchAsync(IEnumerable<StationEventRequest> requests, CancellationToken ct = default);
}

public interface IAdminOverrideService
{
    Task<AdminOverrideResponse> ProcessAsync(AdminOverrideRequest request, CancellationToken ct = default);
}

public interface IHeartbeatService
{
    Task RecordAsync(HeartbeatRequest request, CancellationToken ct = default);
}

public interface IStationBootstrapService
{
    Task<StationBootstrapResponse> GetBootstrapAsync(string stationCode, string line, CancellationToken ct = default);
}

public interface IStationRegistrationService
{
    Task<RegisterStationResponse> RegisterAsync(RegisterStationRequest request, CancellationToken ct = default);
}

public interface IAdminAuthService
{
    /// <summary>
    /// Valida usuario + contraseña contra <c>usuarios_sistema</c> (SHA-256) y decide si
    /// el usuario tiene privilegios de administrador según departamento/cargo configurados.
    /// Nunca lanza por credenciales inválidas: devuelve la respuesta con Authenticated=false.
    /// </summary>
    Task<AdminLoginResponse> LoginAsync(AdminLoginRequest request, CancellationToken ct = default);
}
