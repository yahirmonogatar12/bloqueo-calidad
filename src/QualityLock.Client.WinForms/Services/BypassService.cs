using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QualityLock.Client.WinForms.Models;
using QualityLock.Shared.Constants;

namespace QualityLock.Client.WinForms.Services;

public class BypassService(string hmacSecret)
{
    public (bool valid, string reason) ValidateBypass(string stationCode)
    {
        try
        {
            if (!File.Exists(AppConstants.BypassFile))
                return (false, "bypass.json not found.");

            var json = File.ReadAllText(AppConstants.BypassFile);
            var token = JsonSerializer.Deserialize<BypassToken>(json);

            if (token is null || !token.Enabled)
                return (false, "Bypass not enabled.");

            if (!string.Equals(token.StationCode, stationCode, StringComparison.OrdinalIgnoreCase))
                return (false, "Bypass station code mismatch.");

            if (token.ExpiresAtUtc < DateTime.UtcNow)
                return (false, "Bypass has expired.");

            if (!VerifySignature(token))
                return (false, "Bypass signature invalid.");

            return (true, token.Reason);
        }
        catch (Exception ex)
        {
            return (false, $"Bypass error: {ex.Message}");
        }
    }

    private bool VerifySignature(BypassToken token)
    {
        var payload = $"{token.StationCode}|{token.IssuedBy}|{token.ExpiresAtUtc:O}|{token.Reason}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacSecret));
        var computed = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        return string.Equals(computed, token.Signature, StringComparison.Ordinal);
    }
}
