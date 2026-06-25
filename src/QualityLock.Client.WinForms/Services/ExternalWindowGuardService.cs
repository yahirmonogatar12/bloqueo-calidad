using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace QualityLock.Client.WinForms.Services;

public sealed class ExternalWindowGuardService
{
    private readonly WindowAccessGuardOptions _options;

    public ExternalWindowGuardService(WindowAccessGuardOptions options)
    {
        _options = options;
    }

    public bool Enabled => _options.Enabled && _options.Rules.Count > 0;
    public int PollMilliseconds => Math.Max(250, _options.PollMilliseconds);

    // Estado por-handle para reglas PromptAuthorization.
    private readonly Dictionary<IntPtr, AuthorizedWindow> _authorized = []; // revelada por un autorizador
    private readonly HashSet<IntPtr> _pending = [];                         // overlay abierto para este hwnd

    public event Action<WindowBlockedEvent>? WindowBlocked;
    public event Action<AuthorizationRequest>? AuthorizationRequired;
    /// <summary>Se dispara cuando una ventana autorizada se cierra, con el tiempo que estuvo abierta.</summary>
    public event Action<AuthorizationClosedEvent>? AuthorizationClosed;

    /// <summary>Autorizador válido: recuerda la ventana como autorizada (no se vuelve a pedir).</summary>
    public void MarkAuthorized(IntPtr hwnd, AuthorizationRequest req, string? badgeCode, string? displayName, string? role)
    {
        _pending.Remove(hwnd);
        _authorized[hwnd] = new AuthorizedWindow(
            req.Rule.Name, req.ProcessName, req.WindowTitle, badgeCode, displayName, role, DateTime.UtcNow);
    }

    /// <summary>Autorización cancelada: cierra la ventana restringida (no se autorizó).</summary>
    public void CancelAuthorization(IntPtr hwnd)
    {
        _pending.Remove(hwnd);
        Apply(WindowBlockAction.Close, hwnd);
    }

    public static IReadOnlyList<OpenWindowSnapshot> GetOpenWindows()
        => EnumerateTopLevelWindows()
            .Select(w => new OpenWindowSnapshot(w.ProcessName, w.Title))
            .Distinct()
            .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void Check(string? badgeCode, string? role)
    {
        if (!Enabled) return;

        var seen = new HashSet<IntPtr>();
        foreach (var window in EnumerateTopLevelWindows())
        {
            seen.Add(window.Handle);
            foreach (var rule in _options.Rules)
            {
                if (!rule.Matches(window.ProcessName, window.Title))
                    continue;

                if (rule.Allows(badgeCode, role))
                    continue;

                if (rule.BlockAction == WindowBlockAction.PromptAuthorization)
                {
                    if (_authorized.ContainsKey(window.Handle)) continue; // ya autorizada
                    if (_pending.Contains(window.Handle)) continue;       // overlay abierto

                    // No ocultamos la ventana (SW_HIDE la sacaría del enum -> no la
                    // volveríamos a ver al cancelar). El overlay topmost a pantalla
                    // completa la tapa mientras se pide autorización.
                    _pending.Add(window.Handle);
                    AuthorizationRequired?.Invoke(new AuthorizationRequest(
                        rule, window.ProcessName, window.Title, window.Handle));
                    break;
                }

                Apply(rule.BlockAction, window.Handle);
                WindowBlocked?.Invoke(new WindowBlockedEvent(
                    rule.Name,
                    window.ProcessName,
                    window.Title,
                    badgeCode,
                    role,
                    rule.BlockAction));
                break;
            }
        }

        // Barrido: al cerrarse la ventana su HWND desaparece del enum. Una ventana
        // autorizada que ya no aparece = se cerró -> registrar el tiempo que estuvo
        // abierta y olvidarla (al reabrir vuelve a pedir autorización).
        var closed = _authorized.Where(kv => !seen.Contains(kv.Key)).ToList();
        foreach (var (handle, w) in closed)
        {
            _authorized.Remove(handle);
            var seconds = (int)Math.Max(0, (DateTime.UtcNow - w.AuthorizedAtUtc).TotalSeconds);
            AuthorizationClosed?.Invoke(new AuthorizationClosedEvent(
                w.RuleName, w.ProcessName, w.WindowTitle, w.BadgeCode, w.DisplayName, w.Role, seconds));
        }
        _pending.IntersectWith(seen);
    }

    private static void Apply(WindowBlockAction action, IntPtr hwnd)
    {
        switch (action)
        {
            case WindowBlockAction.Hide:
                ShowWindow(hwnd, SW_HIDE);
                break;
            case WindowBlockAction.Minimize:
                ShowWindow(hwnd, SW_MINIMIZE);
                break;
            default:
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                break;
        }
    }

    private static IEnumerable<ExternalWindowInfo> EnumerateTopLevelWindows()
    {
        var windows = new List<ExternalWindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var titleLength = GetWindowTextLength(hwnd);
            if (titleLength <= 0)
                return true;

            var title = GetWindowTitle(hwnd, titleLength);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0)
                return true;

            var processName = GetProcessName(processId);
            if (string.IsNullOrWhiteSpace(processName))
                return true;

            windows.Add(new ExternalWindowInfo(hwnd, processName, title));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static string GetWindowTitle(IntPtr hwnd, int titleLength)
    {
        var builder = new StringBuilder(titleLength + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string? GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ExternalWindowInfo(IntPtr Handle, string ProcessName, string Title);

    private const int WM_CLOSE = 0x0010;
    private const int SW_HIDE = 0;
    private const int SW_MINIMIZE = 6;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}

public sealed record WindowBlockedEvent(
    string RuleName,
    string ProcessName,
    string WindowTitle,
    string? BadgeCode,
    string? Role,
    WindowBlockAction Action);

public sealed record OpenWindowSnapshot(string ProcessName, string WindowTitle);

public sealed record AuthorizationRequest(
    WindowAccessRule Rule,
    string ProcessName,
    string WindowTitle,
    IntPtr Handle);

/// <summary>Datos de una ventana autorizada, para medir el tiempo que estuvo abierta.</summary>
internal sealed record AuthorizedWindow(
    string RuleName,
    string ProcessName,
    string WindowTitle,
    string? BadgeCode,
    string? DisplayName,
    string? Role,
    DateTime AuthorizedAtUtc);

/// <summary>Una ventana autorizada se cerró; incluye los segundos que estuvo abierta.</summary>
public sealed record AuthorizationClosedEvent(
    string RuleName,
    string ProcessName,
    string WindowTitle,
    string? BadgeCode,
    string? DisplayName,
    string? Role,
    int OpenSeconds);
