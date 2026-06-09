using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QualityLock.Api.Auth;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    ITokenService tokenService,
    IAdminAuthService adminAuth,
    IOptions<ClientAuthOptions> clientAuth) : ControllerBase
{
    private readonly ClientAuthOptions _clientAuth = clientAuth.Value;

    [HttpPost("token")]
    [AllowAnonymous]
    public ActionResult<TokenResponse> Token([FromBody] TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StationCode) || string.IsNullOrWhiteSpace(request.ApiKey))
            return BadRequest(new ApiErrorDto("INVALID_REQUEST", "StationCode and ApiKey are required."));

        if (string.IsNullOrEmpty(_clientAuth.ClientApiKey) || !ApiKeyMatches(request.ApiKey))
            return Unauthorized(new ApiErrorDto("INVALID_API_KEY", "Invalid client API key."));

        var (token, expires) = tokenService.IssueStationToken(request.StationCode.Trim());
        return Ok(new TokenResponse(token, expires));
    }

    /// <summary>
    /// Login de administrador contra usuarios_sistema. Requiere el token de estación
    /// (lo invoca el cliente ya autenticado). Devuelve si el usuario es admin según
    /// los departamentos/cargos configurados. No revela si el usuario existe.
    /// </summary>
    [HttpPost("admin-login")]
    public async Task<ActionResult<AdminLoginResponse>> AdminLogin(
        [FromBody] AdminLoginRequest request, CancellationToken ct)
    {
        var result = await adminAuth.LoginAsync(request, ct);
        return result.Authenticated && result.IsAdmin
            ? Ok(result)
            : Unauthorized(result);
    }

    /// <summary>
    /// Login de un usuario cualquiera (no necesariamente admin) contra usuarios_sistema.
    /// Usado por la estacion cuando se detecta desbloqueo por tecleo manual (no escaner):
    /// se exige la contrasena del propio usuario. Devuelve 200 si la contrasena es
    /// correcta (aunque no sea admin), 401 si no.
    /// </summary>
    [HttpPost("user-login")]
    public async Task<ActionResult<AdminLoginResponse>> UserLogin(
        [FromBody] AdminLoginRequest request, CancellationToken ct)
    {
        var result = await adminAuth.LoginAsync(request, ct);
        return result.Authenticated
            ? Ok(result)
            : Unauthorized(result);
    }

    /// <summary>Constant-time comparison to avoid timing leaks on the shared API key.</summary>
    private bool ApiKeyMatches(string provided)
    {
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(_clientAuth.ClientApiKey);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
