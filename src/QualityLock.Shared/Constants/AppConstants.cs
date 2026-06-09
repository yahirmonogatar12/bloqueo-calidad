namespace QualityLock.Shared.Constants;

public static class AppConstants
{
    public const string CorrelationIdHeader = "X-Correlation-Id";
    public const string LocalDataPath = @"C:\ProgramData\QualityLock";
    public const string ClientStateFile = @"C:\ProgramData\QualityLock\client-state.json";
    public const string OperatorCacheFile = @"C:\ProgramData\QualityLock\operator-cache.json";
    public const string EventQueueFile = @"C:\ProgramData\QualityLock\event-queue.jsonl";
    public const string BypassFile = @"C:\ProgramData\QualityLock\bypass.json";
    public const string LogsFolder = @"C:\ProgramData\QualityLock\logs";

    public const int CacheExpirationHours = 24;
    public const int HeartbeatIntervalSeconds = 60;

    /// <summary>
    /// Cada cuánto se refresca la caché de usuarios (operator-cache.json) desde el
    /// bootstrap, para que usuarios nuevos esten disponibles offline sin reiniciar la
    /// estacion. Se evalua en el tick del heartbeat.
    /// </summary>
    public const int OperatorCacheRefreshMinutes = 15;
    /// <summary>Valor por defecto del auto-bloqueo por inactividad (segundos), si la
    /// estacion no define <c>AutoLockSeconds</c> en su appsettings.json.</summary>
    public const int AutoLockInactivitySeconds = 300;
    public const int SafeModeFailureThreshold = 3;
    public const int SafeModeWindowMinutes = 10;
}

public static class ApiRoutes
{
    public const string BadgesValidate = "/api/badges/validate";
    public const string SessionsStart = "/api/sessions/start";
    public const string SessionsEnd = "/api/sessions/end";
    public const string Events = "/api/events";
    public const string AdminOverride = "/api/admin/override";
    public const string Heartbeats = "/api/heartbeats";
    public const string StationBootstrap = "/api/stations/{stationCode}/bootstrap";
    public const string Health = "/health";
}
