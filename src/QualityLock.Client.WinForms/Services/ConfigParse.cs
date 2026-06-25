using Microsoft.Extensions.Configuration;

namespace QualityLock.Client.WinForms.Services;

/// <summary>Helpers compartidos para leer valores de IConfiguration con valor por defecto.</summary>
internal static class ConfigParse
{
    public static int PositiveInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    public static int NonNegativeInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : fallback;

    public static T Enum<T>(string? value, T fallback) where T : struct
        => System.Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    public static IReadOnlyList<string> List(IConfigurationSection section)
        => section.GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToArray();
}
