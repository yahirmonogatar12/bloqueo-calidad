namespace QualityLock.Api.Auth;

/// <summary>Opciones de firma y emisión de tokens JWT (sección "Jwt" de configuración).</summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "QualityLock.Api";
    public string Audience { get; set; } = "QualityLock.Clients";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 720;
}

/// <summary>Opciones de autenticación de clientes (sección "Auth").</summary>
public class ClientAuthOptions
{
    /// <summary>Clave compartida que una estación presenta para obtener un token.</summary>
    public string ClientApiKey { get; set; } = string.Empty;
}
