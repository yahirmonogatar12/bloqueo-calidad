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
    public bool AllowManualLogin => _options.AllowManualLogin;

    // Estado por (regla + proceso) para reglas PromptAuthorization. Se autoriza el PROCESO,
    // no el HWND: multitool abre/cierra subventanas (handles distintos) sin re-pedir auth.
    // El ajuste se cronometra hasta que el proceso ya no tiene ninguna ventana de esa regla.
    private readonly Dictionary<AuthKey, AuthorizedWindow> _authorized = []; // revelada por un autorizador
    private readonly HashSet<AuthKey> _pending = [];                         // overlay abierto para este proceso+regla

    private readonly record struct AuthKey(string RuleName, uint ProcessId);

    public event Action<WindowBlockedEvent>? WindowBlocked;
    public event Action<AuthorizationRequest>? AuthorizationRequired;
    /// <summary>Se dispara cuando una ventana autorizada se cierra, con el tiempo que estuvo abierta.</summary>
    public event Action<AuthorizationClosedEvent>? AuthorizationClosed;

    /// <summary>Autorizador válido: recuerda el PROCESO como autorizado (no se vuelve a pedir
    /// para ninguna ventana suya que matchee la regla, incl. subventanas que abra después).</summary>
    public void MarkAuthorized(IntPtr hwnd, AuthorizationRequest req, string? badgeCode, string? displayName, string? role)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        var key = new AuthKey(req.Rule.Name, pid);
        _pending.Remove(key);
        _authorized[key] = new AuthorizedWindow(
            req.Rule.Name, req.ProcessName, req.WindowTitle, badgeCode, displayName, role, DateTime.UtcNow);
    }

    /// <summary>Autorización cancelada: cierra la ventana restringida (no se autorizó).</summary>
    public void CancelAuthorization(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        // No conocemos la regla aquí; quitamos cualquier pendiente de este proceso.
        _pending.RemoveWhere(k => k.ProcessId == pid);
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

        // Claves (regla+proceso) con al menos una ventana viva en este barrido. El proceso
        // sigue "presente" mientras tenga cualquier ventana de la regla (su principal o una
        // subventana), así que cerrar y reabrir subventanas no corta el ajuste.
        var seenKeys = new HashSet<AuthKey>();
        foreach (var window in EnumerateTopLevelWindows())
        {
            foreach (var rule in _options.Rules)
            {
                if (!rule.Matches(window.ProcessName, window.Title))
                    continue;

                // TrackOnly: no bloquea ni pide autorización. Solo cronometra: rastrea el
                // proceso para que el barrido de cierre emita AuthorizationClosed con el tiempo.
                if (rule.BlockAction == WindowBlockAction.TrackOnly)
                {
                    var trackKey = new AuthKey(rule.Name, window.ProcessId);
                    seenKeys.Add(trackKey);
                    if (!_authorized.ContainsKey(trackKey))
                    {
                        _authorized[trackKey] = new AuthorizedWindow(
                            rule.Name, window.ProcessName, window.Title, badgeCode, null, role, DateTime.UtcNow);
                    }
                    break;
                }

                if (rule.BlockAction == WindowBlockAction.PromptAuthorization)
                    seenKeys.Add(new AuthKey(rule.Name, window.ProcessId));

                if (rule.Allows(badgeCode, role))
                {
                    // El operador ya tiene permiso, así que no se le pide autorización.
                    // Aun así cronometramos el tiempo de ajuste: rastreamos el proceso como
                    // autorizado para que el barrido de cierre emita AuthorizationClosed.
                    var autoKey = new AuthKey(rule.Name, window.ProcessId);
                    if (rule.BlockAction == WindowBlockAction.PromptAuthorization &&
                        !_authorized.ContainsKey(autoKey))
                    {
                        _authorized[autoKey] = new AuthorizedWindow(
                            rule.Name, window.ProcessName, window.Title, badgeCode, null, role, DateTime.UtcNow);
                    }
                    continue;
                }

                if (rule.BlockAction == WindowBlockAction.PromptAuthorization)
                {
                    var key = new AuthKey(rule.Name, window.ProcessId);
                    if (_authorized.ContainsKey(key)) continue; // proceso ya autorizado
                    if (_pending.Contains(key)) continue;       // overlay abierto

                    // No ocultamos la ventana (SW_HIDE la sacaría del enum -> no la
                    // volveríamos a ver al cancelar). El overlay topmost a pantalla
                    // completa la tapa mientras se pide autorización.
                    _pending.Add(key);
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

        // Barrido: un proceso autorizado cuya regla ya no tiene NINGUNA ventana viva =
        // se cerró la ventana de ajuste -> registrar el tiempo total y olvidarlo (al
        // reabrir vuelve a pedir autorización).
        var closed = _authorized.Where(kv => !seenKeys.Contains(kv.Key)).ToList();
        foreach (var (key, w) in closed)
        {
            _authorized.Remove(key);
            var seconds = (int)Math.Max(0, (DateTime.UtcNow - w.AuthorizedAtUtc).TotalSeconds);
            AuthorizationClosed?.Invoke(new AuthorizationClosedEvent(
                w.RuleName, w.ProcessName, w.WindowTitle, w.BadgeCode, w.DisplayName, w.Role, seconds));
        }
        _pending.IntersectWith(seenKeys);
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

            windows.Add(new ExternalWindowInfo(hwnd, processId, processName, title));
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

    private sealed record ExternalWindowInfo(IntPtr Handle, uint ProcessId, string ProcessName, string Title);

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
