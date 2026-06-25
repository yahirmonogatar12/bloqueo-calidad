using Microsoft.Extensions.Configuration;

namespace QualityLock.Client.WinForms.Services;

public sealed class QrInputFocusOptions
{
    public bool Enabled { get; init; }
    public int DelayMilliseconds { get; init; } = 500;
    public int RetryCount { get; init; } = 3;
    public string ProcessName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public WindowTitleMatchMode MatchMode { get; init; } = WindowTitleMatchMode.Exact;
    public QrInputTarget Input { get; init; } = new();

    public bool IsConfigured =>
        Enabled &&
        (!string.IsNullOrWhiteSpace(ProcessName) || !string.IsNullOrWhiteSpace(WindowTitle));

    public static QrInputFocusOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("QrInputFocus");
        if (!section.Exists())
            return new QrInputFocusOptions();

        return new QrInputFocusOptions
        {
            Enabled = string.Equals(section["Enabled"], "true", StringComparison.OrdinalIgnoreCase),
            DelayMilliseconds = ReadNonNegativeInt(section["DelayMilliseconds"], 500),
            RetryCount = ReadPositiveInt(section["RetryCount"], 3),
            ProcessName = section["ProcessName"] ?? "",
            WindowTitle = section["WindowTitle"] ?? section["Title"] ?? "",
            MatchMode = ParseEnum(section["MatchMode"], WindowTitleMatchMode.Exact),
            Input = QrInputTarget.FromConfiguration(section.GetSection("Input"))
        };
    }

    private static int ReadPositiveInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static int ReadNonNegativeInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : fallback;

    private static T ParseEnum<T>(string? value, T fallback) where T : struct
        => Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

public sealed class QrInputTarget
{
    public string AutomationId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string ControlType { get; init; } = "";
    public int ControlIndex { get; init; }
    public int NativeWindowHandle { get; init; }
    public bool UseFallbackClick { get; init; }
    public int FallbackClickX { get; init; } = -1;
    public int FallbackClickY { get; init; } = -1;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(AutomationId) &&
        string.IsNullOrWhiteSpace(Name) &&
        string.IsNullOrWhiteSpace(ClassName) &&
        string.IsNullOrWhiteSpace(ControlType) &&
        ControlIndex <= 0 &&
        NativeWindowHandle <= 0;

    public bool HasFallbackClick => UseFallbackClick && FallbackClickX >= 0 && FallbackClickY >= 0;

    public static QrInputTarget FromConfiguration(IConfiguration section)
        => new()
        {
            AutomationId = section["AutomationId"] ?? "",
            Name = section["Name"] ?? "",
            ClassName = section["ClassName"] ?? "",
            ControlType = section["ControlType"] ?? "",
            ControlIndex = int.TryParse(section["ControlIndex"], out var index) && index >= 0 ? index : 0,
            NativeWindowHandle = int.TryParse(section["NativeWindowHandle"], out var handle) ? handle : 0,
            UseFallbackClick = string.Equals(section["UseFallbackClick"], "true", StringComparison.OrdinalIgnoreCase),
            FallbackClickX = int.TryParse(section["FallbackClickX"], out var x) ? x : -1,
            FallbackClickY = int.TryParse(section["FallbackClickY"], out var y) ? y : -1
        };
}
