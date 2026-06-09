using QualityLock.Shared.Constants;

namespace QualityLock.Client.WinForms.Services;

/// <summary>
/// Mantiene fresca la caché local de usuarios (operator-cache.json) que se usa para el
/// desbloqueo offline. La refresca desde el bootstrap de la estación cada cierto tiempo
/// (<see cref="AppConstants.OperatorCacheRefreshMinutes"/>) para que los usuarios nuevos
/// queden disponibles sin reiniciar la estación. Tambien permite refrescar bajo demanda.
/// </summary>
public class OperatorCacheService(ApiClientService api, LocalStateService localState, string stationCode)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    /// <summary>True si ya paso el intervalo de refresco desde la ultima vez.</summary>
    public bool IsDue =>
        DateTime.UtcNow - _lastRefreshUtc >= TimeSpan.FromMinutes(AppConstants.OperatorCacheRefreshMinutes);

    /// <summary>Refresca solo si toca (pensado para llamarse en cada heartbeat).</summary>
    public Task<bool> RefreshIfDueAsync(CancellationToken ct = default)
        => IsDue ? RefreshNowAsync(ct) : Task.FromResult(false);

    /// <summary>
    /// Refresca la caché de inmediato desde el bootstrap. No-op si el backend no responde
    /// (se conserva la caché previa). Devuelve true si se actualizo. No se solapa.
    /// </summary>
    public async Task<bool> RefreshNowAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
            return false;

        try
        {
            var bootstrap = await api.GetBootstrapAsync(stationCode, ct);
            if (bootstrap is null)
                return false;   // offline: mantenemos la caché anterior

            localState.SaveOperatorCache(bootstrap.AllowedOperators);
            _lastRefreshUtc = DateTime.UtcNow;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }
}
