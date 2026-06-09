using System.Security.Cryptography;
using System.Text;
using QualityLock.Shared.DTOs;

namespace QualityLock.Client.WinForms.Services;

/// <summary>
/// Resultado de un intento de autenticación de administrador.
/// </summary>
public readonly record struct AdminAuthResult(bool Authenticated, string DisplayName, string Message)
{
    public static AdminAuthResult Fail(string message) => new(false, string.Empty, message);
    public static AdminAuthResult Ok(string displayName) => new(true, displayName, string.Empty);
}

/// <summary>
/// Controla el acceso de administrador para abrir la configuración de estación,
/// detener el servicio y registrar overrides.
///
/// Camino principal (online): valida usuario + contraseña contra <c>usuarios_sistema</c>
/// vía la API, que además aplica el gating por departamento/cargo.
///
/// Respaldo (offline): si el backend no responde, valida el PIN local configurado
/// (<c>AdminPin</c> / <c>QUALITYLOCK_ADMIN_PIN</c>) para no bloquear la recuperación
/// de una estación sin red. Conserva el nombre <c>AdminPinService</c> por compatibilidad.
/// </summary>
public class AdminPinService(ApiClientService api, string? configuredPin)
{
    // Usado solo cuando no se configura nada, para no dejar fuera una instalación nueva.
    private const string DefaultPin = "admin1234";

    private readonly string _pin = ResolvePin(configuredPin);

    /// <summary>True cuando corre con el PIN por defecto inseguro (no se configuró PIN).</summary>
    public bool IsUsingDefault { get; } = string.IsNullOrWhiteSpace(configuredPin)
        && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QUALITYLOCK_ADMIN_PIN"));

    private static string ResolvePin(string? configuredPin)
    {
        if (!string.IsNullOrWhiteSpace(configuredPin))
            return configuredPin.Trim();

        var fromEnv = Environment.GetEnvironmentVariable("QUALITYLOCK_ADMIN_PIN");
        return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv.Trim() : DefaultPin;
    }

    /// <summary>
    /// Autentica a un administrador. El PIN local funciona SIEMPRE (online u offline):
    /// si la contraseña ingresada coincide con el PIN, concede acceso de inmediato. Si no
    /// es el PIN y hay backend, valida usuario+contraseña contra usuarios_sistema.
    /// </summary>
    public async Task<AdminAuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            return AdminAuthResult.Fail("Ingrese la contraseña.");

        // 1) PIN local: válido siempre (atajo de administrador de planta).
        if (ValidatePin(password))
            return AdminAuthResult.Ok("Administrador (PIN local)");

        // 2) Si hay backend, intentar login de usuario contra usuarios_sistema.
        var online = await api.IsAvailableAsync(ct);
        if (online)
        {
            if (string.IsNullOrWhiteSpace(username))
                return AdminAuthResult.Fail("Ingrese el usuario, o el PIN local.");

            var resp = await api.AdminLoginAsync(new AdminLoginRequest(username.Trim(), password), ct);
            if (resp is not null)
            {
                if (resp.Authenticated && resp.IsAdmin)
                    return AdminAuthResult.Ok(resp.DisplayName);

                return AdminAuthResult.Fail(resp.Message ?? "Usuario o contraseña inválidos.");
            }
        }

        // 3) Sin backend y sin PIN válido.
        return AdminAuthResult.Fail("Sin conexión: use el PIN local.");
    }

    /// <summary>Comparación de PIN en tiempo constante (respaldo offline).</summary>
    public bool ValidatePin(string entered)
    {
        if (string.IsNullOrEmpty(entered)) return false;
        var a = Encoding.UTF8.GetBytes(entered);
        var b = Encoding.UTF8.GetBytes(_pin);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
