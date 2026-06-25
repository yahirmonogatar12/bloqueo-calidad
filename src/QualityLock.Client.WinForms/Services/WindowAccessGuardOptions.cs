using Microsoft.Extensions.Configuration;

namespace QualityLock.Client.WinForms.Services;

public sealed class WindowAccessGuardOptions
{
    public bool Enabled { get; init; } = true;
    public int PollMilliseconds { get; init; } = 500;
    // Si false, el overlay de autorización solo acepta escáner de gafete (oculta la vía
    // usuario+contraseña). Útil para estaciones donde solo se autoriza por gafete.
    public bool AllowManualLogin { get; init; } = true;
    public IReadOnlyList<WindowAccessRule> Rules { get; init; } = DefaultRules();

    public static WindowAccessGuardOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("WindowAccessGuard");
        if (!section.Exists())
            return new WindowAccessGuardOptions();

        var enabled = !string.Equals(section["Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        var allowManualLogin = !string.Equals(section["AllowManualLogin"], "false", StringComparison.OrdinalIgnoreCase);
        var pollMilliseconds = int.TryParse(section["PollMilliseconds"], out var poll) && poll > 0
            ? poll
            : 500;

        var rules = new List<WindowAccessRule>();
        foreach (var child in section.GetSection("Rules").GetChildren())
        {
            var rule = WindowAccessRule.FromConfiguration(child);
            if (rule is not null)
                rules.Add(rule);
        }

        return new WindowAccessGuardOptions
        {
            Enabled = enabled,
            PollMilliseconds = pollMilliseconds,
            AllowManualLogin = allowManualLogin,
            Rules = rules.Count > 0 ? rules : DefaultRules()
        };
    }

    private static IReadOnlyList<WindowAccessRule> DefaultRules() =>
    [
        new WindowAccessRule
        {
            Name = "AT-01 Error View",
            ProcessName = "inctest",
            WindowTitle = "Error View",
            MatchMode = WindowTitleMatchMode.Exact,
            BlockAction = WindowBlockAction.PromptAuthorization,
            AllowedRoles = ["superadmin", "Tecnico QA"]
        }
    ];
}

public sealed class WindowAccessRule
{
    public string Name { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public WindowTitleMatchMode MatchMode { get; init; } = WindowTitleMatchMode.Exact;
    public WindowBlockAction BlockAction { get; init; } = WindowBlockAction.Close;
    public IReadOnlyList<string> AllowedRoles { get; init; } = [];
    public IReadOnlyList<string> AllowedUsers { get; init; } = [];

    public static WindowAccessRule? FromConfiguration(IConfiguration section)
    {
        var title = section["WindowTitle"] ?? section["Title"] ?? "";
        var processName = section["ProcessName"] ?? "";

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(processName))
            return null;

        return new WindowAccessRule
        {
            Name = section["Name"] ?? title,
            ProcessName = processName,
            WindowTitle = title,
            MatchMode = ConfigParse.Enum(section["MatchMode"], WindowTitleMatchMode.Exact),
            BlockAction = ConfigParse.Enum(section["BlockAction"] ?? section["Action"], WindowBlockAction.Close),
            AllowedRoles = ConfigParse.List(section.GetSection("AllowedRoles")),
            AllowedUsers = ConfigParse.List(section.GetSection("AllowedUsers"))
        };
    }

    public bool Matches(string processName, string windowTitle)
    {
        if (!string.IsNullOrWhiteSpace(ProcessName) &&
            !string.Equals(NormalizeProcessName(ProcessName), NormalizeProcessName(processName),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(WindowTitle))
            return true;

        return MatchMode switch
        {
            WindowTitleMatchMode.Contains => windowTitle.Contains(WindowTitle, StringComparison.OrdinalIgnoreCase),
            WindowTitleMatchMode.StartsWith => windowTitle.StartsWith(WindowTitle, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(windowTitle, WindowTitle, StringComparison.OrdinalIgnoreCase)
        };
    }

    public bool Allows(string? badgeCode, string? role)
    {
        if (AllowedRoles.Count == 0 && AllowedUsers.Count == 0)
            return true;

        if (!string.IsNullOrWhiteSpace(role) && Contains(AllowedRoles, role))
            return true;

        if (!string.IsNullOrWhiteSpace(badgeCode) && Contains(AllowedUsers, badgeCode))
            return true;

        return false;
    }

    private static string NormalizeProcessName(string value)
        => Path.GetFileNameWithoutExtension(value.Trim());

    private static bool Contains(IReadOnlyList<string> allowed, string value)
    {
        foreach (var entry in allowed)
        {
            if (string.Equals(entry.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

}

public enum WindowTitleMatchMode
{
    Exact,
    Contains,
    StartsWith
}

public enum WindowBlockAction
{
    Close,
    Hide,
    Minimize,
    PromptAuthorization,
    // No bloquea ni pide nada: solo cronometra el tiempo que la ventana estuvo abierta
    // (para estaciones sin personal, ej. Vision).
    TrackOnly
}
