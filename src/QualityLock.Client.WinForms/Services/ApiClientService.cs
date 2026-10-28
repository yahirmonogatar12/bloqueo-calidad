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
    /// Returns a valid bearer token, acquiring a new one when missing or near expiry.
    /// Returns null if a token could not be obtained (e.g. backend offline) — callers
    /// should treat that as "offline".
    /// ConfigureAwait(false): quien renueva tiene _tokenLock. Si la renovacion la empezo la
    /// UI (heartbeat) y la UI se traba, sin esto el candado quedaba tomado y la verificacion
    /// MES del hilo del escaner esperaba hasta sus 2 s ("Sin respuesta del MES").
    /// </summary>
    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        var token = _accessToken;
        if (token is not null && DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-2))
            return token;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            token = _accessToken;
            if (token is not null && DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-2))
                return token;

            var resp = await http.PostAsJsonAsync("api/auth/token",
                new TokenRequest(_stationCode, clientApiKey), ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return null;

            var body = await resp.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts, ct).ConfigureAwait(false);
            if (body is null || string.IsNullOrEmpty(body.AccessToken))
                return null;

            _tokenExpiresUtc = body.ExpiresAtUtc;
            _accessToken = body.AccessToken;
            return body.AccessToken;
        }
        catch
        {
            return null;
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
    }

    /// <summary>
    /// Olvida el token solo si sigue siendo el rechazado: un 401 atrasado de una peticion
    /// vieja no debe tirar el token que otra peticion ya renovo.
    /// </summary>
    private void InvalidateToken(string rejected)
        => Interlocked.CompareExchange(ref _accessToken, null, rejected);

    /// <summary>
    /// Sends an authenticated request, transparently acquiring a token first and
    /// retrying once if the server rejects the token with 401.
    /// El token va en cada peticion (no en DefaultRequestHeaders, que no se puede cambiar
    /// mientras otra peticion lo lee): asi el servicio se puede usar desde cualquier hilo.
    /// La verificacion MES corre en el hilo del escaner, no en el de la UI.
    /// </summary>
    private async Task<HttpResponseMessage?> SendAuthedAsync(
        Func<HttpRequestMessage> build, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);
        if (token is null)
            return null;

        var resp = await SendWithTokenAsync(build, token, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            InvalidateToken(token);
            token = await GetTokenAsync(ct).ConfigureAwait(false);
            if (token is null)
                return null;
            resp = await SendWithTokenAsync(build, token, ct).ConfigureAwait(false);
        }
        return resp;
    }

    private Task<HttpResponseMessage> SendWithTokenAsync(
        Func<HttpRequestMessage> build, string token, CancellationToken ct)
    {
        var request = build();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http.SendAsync(request, ct);
    }

    private static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    // JsonContent.Create sin opciones serializa igual que PostAsJsonAsync (JsonSerializerDefaults.Web).
    private static HttpRequestMessage Json<T>(HttpMethod method, string url, T body)
        => new(method, url) { Content = JsonContent.Create(body) };

    // ─────────────────────────────────────────────────────────
    // API calls
    // ─────────────────────────────────────────────────────────

    public async Task<StationBootstrapResponse?> GetBootstrapAsync(string stationCodeArg, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => Get($"api/stations/{stationCodeArg}/bootstrap?line={Uri.EscapeDataString(Line)}"), ct);
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
                () => Json(HttpMethod.Post, "api/badges/validate", request), ct);
            if (resp is null || !resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<BadgeValidationResponse>(JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Verificacion MES (Vision OK) de un codigo antes de teclearlo. Null si no hay respuesta.</summary>
    public async Task<VisionCheckResponse?> CheckVisionAsync(string code, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => Get($"api/mes/vision-check?code={Uri.EscapeDataString(code)}&line={Uri.EscapeDataString(Line)}"), ct);
            if (resp is null || !resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<VisionCheckResponse>(JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Registra en el MES una PCB escaneada otra vez en la sesion del COM. False si no se pudo.</summary>
    public async Task<bool> ReportRepeatedScanAsync(
        string code, IReadOnlyList<RepeatedScanItem> scans, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => Json(HttpMethod.Post, "api/mes/repeated-scan", new RepeatedScanRequest(code, Line, scans)), ct);
            return resp is { IsSuccessStatusCode: true };
        }
        catch
        {
            return false;
        }
    }

    public async Task<StartSessionResponse?> StartSessionAsync(StartSessionRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => Json(HttpMethod.Post, "api/sessions/start", request), ct);
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
            await SendAuthedAsync(() => Json(HttpMethod.Post, "api/sessions/end", request), ct);
        }
        catch { }
    }

    /// <summary>Posts a batch of queued offline events. Returns true only on 2xx.</summary>
    public async Task<bool> RecordEventsAsync(IEnumerable<StationEventRequest> events, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => Json(HttpMethod.Post, "api/events", events), ct);
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
            await SendAuthedAsync(() => Json(HttpMethod.Post, "api/heartbeats", request), ct);
        }
        catch { }
    }

    public async Task<AdminOverrideResponse?> AdminOverrideAsync(AdminOverrideRequest request, CancellationToken ct = default)
    {
        try
        {
            var resp = await SendAuthedAsync(
                () => Json(HttpMethod.Post, "api/admin/override", request), ct);
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
                () => Json(HttpMethod.Post, "api/auth/admin-login", request), ct);
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
                () => Json(HttpMethod.Post, "api/auth/user-login", request), ct);
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
                () => Json(HttpMethod.Put, $"api/stations/{request.StationCode}", request), ct);

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
