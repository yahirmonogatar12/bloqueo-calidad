using System.Security.Cryptography;
using System.Text;

namespace QualityLock.Shared.Security;

/// <summary>
/// Verificación de contraseñas compatible con la tabla <c>usuarios_sistema</c> del MES,
/// que almacena <c>SHA-256(password)</c> en hex minúsculas, sin sal (igual que el resto
/// de los sistemas MES). No introducir sal aquí rompería la compatibilidad.
/// </summary>
public static class Sha256Password
{
    /// <summary>Calcula SHA-256 del texto en UTF-8 y lo devuelve en hex minúsculas (64 chars).</summary>
    public static string Hash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Compara la contraseña en claro contra el hash almacenado en tiempo constante
    /// (evita fugas por temporización). Acepta el hash en mayúsculas o minúsculas.
    /// </summary>
    public static bool Verify(string password, string? storedHashHex)
    {
        if (string.IsNullOrWhiteSpace(storedHashHex))
            return false;

        var computed = Encoding.ASCII.GetBytes(Hash(password));
        var stored = Encoding.ASCII.GetBytes(storedHashHex.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }
}
