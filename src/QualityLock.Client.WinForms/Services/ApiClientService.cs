using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QualityLock.Shared.DTOs;

namespace QualityLock.Client.WinForms.Services;

public class ApiClientService(HttpClient http, string stationCode, string clientApiKey)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private string _stationCode = stationCode;
    private string? _accessToken;
    private DateTime _tokenExpiresUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <summary>Linea de produccion (host_name) de esta estacion. Se envia en el bootstrap.</summary>
    public string Line { get; set; } = string.Empty;

    /// <summary>
    /// Updates the station code used when requesting tokens (used during first-time
    /// setup, before StationCode is persisted to config). Forces re-authentication.
    /// </summary>
    public void SetStationCode(string code)
    {
        if (string.Equals(_stationCode, code, StringComparison.OrdinalIgnoreCase)) return;
        _stationCode = code;
        InvalidateToken();
    }

    // ─────────────────────────────────────────────────────────
    // Authentication
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the HttpClient carries a valid bearer token. Acquires a new one
    /// when missing or near expiry. Returns false if a token could not be obtained
    /// (e.g. backend offline) — callers should treat that as "offline".
    /// </summary>
    private async Task<bool> EnsureTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-2))
            return true;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-2))
                return true;

            var resp = await http.PostAsJsonAsync("api/auth/token",
                new TokenRequest(_stationCode, clientApiKey), ct);

            if (!resp.IsSuccessStatusCode)
                return false;

            var body = await resp.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts, ct);
            if (body is null || string.IsNullOrEmpty(body.AccessToken))
                return false;

            _accessToken = body.AccessToken;
            _tokenExpiresUtc = body.ExpiresAtUtc;
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _tokenExpiresUtc = DateTime.MinValue;
        http.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>
    /// Sends an authenticated request, transparently acquiring a token first and
    /// retrying once if the server rejects the token with 401.
    /// </summary>
    private async Task<HttpResponseMessage?> SendAuthedAsync(
        Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        if (!await EnsureTokenAsync(ct))
            return null;

        var resp = await send();
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            if (!await EnsureTokenAsync(ct))
                return null;
            resp = await send();
        }
        return resp;
    }

    // ─────────────────────────────────────────────────────────
    // API calls
    // ─────────────────────────────────────────────────────────

    public async Task<StationBootstrapResponse?> GetBootstrapAsync(string stationCodeArg, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => http.GetAsync($"api/stations/{stationCodeArg}/bootstrap?line={Uri.EscapeDataString(Line)}", ct), ct);
            if (resp is null || !resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<StationBootstrapResponse>(JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<BadgeValidationResponse?> ValidateBadgeAsync(BadgeValidationRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => http.PostAsJsonAsync("api/badges/validate", request, ct), ct);
            if (resp is null || !resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<BadgeValidationResponse>(JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<StartSessionResponse?> StartSessionAsync(StartSessionRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => http.PostAsJsonAsync("api/sessions/start", request, ct), ct);
            if (resp is null || !resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<StartSessionResponse>(JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task EndSessionAsync(EndSessionRequest request, CancellationToken ct = default)
    {
        try
        {
            await SendAuthedAsync(() => http.PostAsJsonAsync("api/sessions/end", request, ct), ct);
        }
        catch { }
    }

    /// <summary>Posts a batch of queued offline events. Returns true only on 2xx.</summary>
    public async Task<bool> RecordEventsAsync(IEnumerable<StationEventRequest> events, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => http.PostAsJsonAsync("api/events", events, ct), ct);
            return resp is not null && resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task SendHeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
    {
        try
        {
            await SendAuthedAsync(() => http.PostAsJsonAsync("api/heartbeats", request, ct), ct);
        }
        catch { }
    }

    public async Task<AdminOverrideResponse?> AdminOverrideAsync(AdminOverrideRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => http.PostAsJsonAsync("api/admin/override", request, ct), ct);
            if (resp is null || !resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<AdminOverrideResponse>(JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Valida usuario + contraseña de administrador contra usuarios_sistema (vía API).
    /// Devuelve la respuesta del backend, o null si está offline / sin token.
    /// </summary>
    public async Task<AdminLoginResponse?> AdminLoginAsync(AdminLoginRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => http.PostAsJsonAsync("api/auth/admin-login", request, ct), ct);
            if (resp is null) return null;

            // 200 (admin OK) y 401 (no admin / credencial inválida) traen el mismo DTO.
            return await resp.Content.ReadFromJsonAsync<AdminLoginResponse>(JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Valida usuario + contraseña de un usuario cualquiera (no necesariamente admin)
    /// contra usuarios_sistema. Usado para desbloqueo por tecleo manual. Devuelve la
    /// respuesta del backend, o null si está offline / sin token.
    /// </summary>
    public async Task<AdminLoginResponse?> UserLoginAsync(AdminLoginRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => http.PostAsJsonAsync("api/auth/user-login", request, ct), ct);
            if (resp is null) return null;
            return await resp.Content.ReadFromJsonAsync<AdminLoginResponse>(JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await http.GetAsync("health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool ok, string message)> RegisterStationAsync(
        RegisterStationRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => http.PutAsJsonAsync($"api/stations/{request.StationCode}", request, ct), ct);

            if (resp is null)
                return (false, "Backend no disponible o credencial de cliente inválida.");

            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<RegisterStationResponse>(JsonOpts, ct);
                return (true, body?.Message ?? "OK");
            }

            var err = await resp.Content.ReadFromJsonAsync<ApiErrorDto>(JsonOpts, ct);
            return (false, err?.Message ?? $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public string BaseAddress => http.BaseAddress?.ToString() ?? string.Empty;
}
