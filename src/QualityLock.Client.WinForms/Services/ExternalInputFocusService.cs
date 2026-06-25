using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace QualityLock.Client.WinForms.Services;

public sealed class ExternalInputFocusService
{
    private readonly QrInputFocusOptions _options;

    public ExternalInputFocusService(QrInputFocusOptions options)
    {
        _options = options;
    }

    public bool Enabled => _options.IsConfigured;

    public string DiagnosticDescription =>
        $"enabled={_options.Enabled}, configured={Enabled}, process='{_options.ProcessName}', " +
        $"title='{_options.WindowTitle}', fallback={_options.Input.HasFallbackClick}, " +
        $"point={_options.Input.FallbackClickX},{_options.Input.FallbackClickY}";

    public async Task<bool> TryFocusAfterUnlockAsync(Action<string>? log = null)
    {
        log?.Invoke($"QR focus start: {DiagnosticDescription}");
        if (!Enabled)
        {
            log?.Invoke("QR focus skipped: not configured/enabled.");
            return false;
        }

        var delay = Math.Clamp(_options.DelayMilliseconds, 0, 10_000);
        log?.Invoke($"QR focus delay: {delay} ms.");
        if (delay > 0)
            await Task.Delay(delay);

        var retries = Math.Clamp(_options.RetryCount, 1, 10);
        for (var attempt = 0; attempt < retries; attempt++)
        {
            log?.Invoke($"QR focus attempt {attempt + 1}/{retries}.");
            if (TryFocusOnce(log))
            {
                log?.Invoke("QR focus success.");
                return true;
            }

            await Task.Delay(250);
        }

        log?.Invoke("QR focus failed after retries.");
        return false;
    }

    public bool TryFocusOnce(Action<string>? log = null)
    {
        var window = FindFirstWindow(_options.ProcessName, _options.WindowTitle, _options.MatchMode);
        if (window is null && _options.Input.HasFallbackClick && !string.IsNullOrWhiteSpace(_options.ProcessName))
        {
            log?.Invoke("QR focus: exact window not found; retrying by process only for fallback click.");
            window = FindFirstWindow(_options.ProcessName, "", WindowTitleMatchMode.Exact);
        }

        if (window is null)
        {
            log?.Invoke($"QR focus: window not found for process='{_options.ProcessName}', title='{_options.WindowTitle}'.");
            return false;
        }

        log?.Invoke($"QR focus: found window process='{window.Value.ProcessName}', title='{window.Value.Title}', handle={window.Value.Handle}.");
        var brought = BringWindowToFront(window.Value.Handle);
        log?.Invoke($"QR focus: SetForegroundWindow={brought}.");
        var focused = TryFocusWin32Input(window.Value.Handle, _options.Input);
        log?.Invoke($"QR focus: win32 input focus={focused}.");

        // Algunas apps legacy aceptan SetFocus/PostMessage pero no dejan listo el
        // control real para el escaner. Si el punto manual existe, usarlo como accion
        // definitiva aunque el foco Win32 haya reportado exito.
        if (_options.Input.HasFallbackClick)
        {
            var clicked = TryFallbackClick(window.Value.Handle, _options.Input, log);
            log?.Invoke($"QR focus: fallback click={clicked}.");
            return clicked || focused;
        }

        return focused;
    }

    public static IReadOnlyList<InputFocusWindowSnapshot> GetOpenWindows()
        => EnumerateTopLevelWindows()
            .Select(w => new InputFocusWindowSnapshot(w.Handle, w.ProcessName, w.Title))
            .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<QrInputCandidate> GetInputCandidates(
        string processName,
        string windowTitle,
        WindowTitleMatchMode matchMode)
    {
        var window = FindFirstWindow(processName, windowTitle, matchMode);
        return window is null ? [] : GetInputCandidates(window.Value.Handle);
    }

    public static IReadOnlyList<QrInputCandidate> GetInputCandidates(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return [];

        var children = EnumerateEditableChildWindows(windowHandle);
        return children.Select((child, index) => new QrInputCandidate(
                Source: "Win32",
                AutomationId: "",
                Name: child.Text,
                ClassName: child.ClassName,
                ControlType: "Win32.Edit",
                ControlIndex: index,
                NativeWindowHandle: ToStableIntHandle(child.Handle)))
            .ToArray();
    }

    public static bool TryFocus(QrInputFocusOptions options)
        => new ExternalInputFocusService(options).TryFocusOnce();

    public static bool TryCaptureFallbackClickPoint(
        string processName,
        string windowTitle,
        WindowTitleMatchMode matchMode,
        out Point clientPoint)
    {
        clientPoint = Point.Empty;

        var window = FindFirstWindow(processName, windowTitle, matchMode);
        if (window is null)
            return false;

        if (!GetCursorPos(out var cursor))
            return false;

        return TryConvertScreenPointToClientPoint(window.Value.Handle, cursor, out clientPoint);
    }

    public static bool TryCaptureFallbackClickPointFromCursor(out InputFocusPointCapture capture)
    {
        capture = new InputFocusPointCapture(
            new InputFocusWindowSnapshot(IntPtr.Zero, "", ""),
            Point.Empty);

        if (!GetCursorPos(out var cursor))
            return false;

        var windowHandle = FindRootWindowAtPoint(cursor);
        if (windowHandle == IntPtr.Zero)
            return false;

        if (!TryGetWindowSnapshot(windowHandle, out var window))
            return false;

        if (!TryConvertScreenPointToClientPoint(windowHandle, cursor, out var clientPoint))
            return false;

        capture = new InputFocusPointCapture(window, clientPoint);
        return true;
    }

    public static bool IsLeftMouseButtonDown()
        => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    public static void WaitForLeftMouseButtonReleased(int timeoutMilliseconds = 750)
        => WaitForLeftButtonReleased(timeoutMilliseconds);

    private static bool TryFocusWin32Input(IntPtr windowHandle, QrInputTarget target)
    {
        var children = EnumerateEditableChildWindows(windowHandle);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (!TargetMatches(child, target, i))
                continue;

            return FocusChildWindow(windowHandle, child.Handle);
        }

        return target.IsEmpty && children.Count > 0 && FocusChildWindow(windowHandle, children[0].Handle);
    }

    private static bool TryFallbackClick(IntPtr windowHandle, QrInputTarget target, Action<string>? log = null)
    {
        if (!target.HasFallbackClick)
            return false;

        WaitForLeftButtonReleased(750);
        if (IsIconic(windowHandle))
            ShowWindow(windowHandle, SW_RESTORE);
        SetForegroundWindow(windowHandle);
        Thread.Sleep(80);

        if (!TryGetFallbackScreenPoint(windowHandle, target, out var point, log))
            return false;

        if (TryFocusEditableChildAtPoint(windowHandle, point, log))
            return true;

        if (TryPostClickToWindowAtPoint(windowHandle, point, log))
            return true;

        // Ultimo recurso: apps legacy (p.ej. inctest) cuyo input es clase 'Static'
        // no aceptan WM_LBUTTON* posteado. Hacemos un clic fisico real con SendInput.
        return TryPhysicalClick(point, log);
    }

    private static bool TryPhysicalClick(NativePoint screenPoint, Action<string>? log)
    {
        // Esperar a que el operador suelte el boton: si sigue abajo, el down/up
        // sintetico se interpreta como arrastre y mueve la ventana.
        WaitForLeftButtonReleased(1500);
        if (IsLeftMouseButtonDown())
        {
            log?.Invoke("QR focus: physical click skipped; left button still held.");
            return false;
        }

        if (!SetCursorPos(screenPoint.X, screenPoint.Y))
        {
            log?.Invoke("QR focus: SetCursorPos failed before physical click.");
            return false;
        }

        Thread.Sleep(30);
        SendMouseClick();
        log?.Invoke($"QR focus: physical click at {screenPoint.X},{screenPoint.Y}.");
        return true;
    }

    private static void SendMouseClick()
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
        inputs[1].type = INPUT_MOUSE;
        inputs[1].U.mi.dwFlags = MOUSEEVENTF_LEFTUP;
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static bool TryGetFallbackScreenPoint(
        IntPtr windowHandle,
        QrInputTarget target,
        out NativePoint screenPoint,
        Action<string>? log)
    {
        screenPoint = new NativePoint(target.FallbackClickX, target.FallbackClickY);

        if (!GetClientRect(windowHandle, out var clientRect))
        {
            log?.Invoke("QR focus: GetClientRect failed before fallback click.");
            return false;
        }

        if (screenPoint.X < clientRect.Left ||
            screenPoint.Y < clientRect.Top ||
            screenPoint.X >= clientRect.Right ||
            screenPoint.Y >= clientRect.Bottom)
        {
            log?.Invoke(
                $"QR focus: fallback point outside client area: {screenPoint.X},{screenPoint.Y}; " +
                $"client={clientRect.Left},{clientRect.Top},{clientRect.Right},{clientRect.Bottom}.");
            return false;
        }

        if (!ClientToScreen(windowHandle, ref screenPoint))
        {
            log?.Invoke("QR focus: ClientToScreen failed.");
            return false;
        }

        if (!TryConvertScreenPointToClientPoint(windowHandle, screenPoint, out _))
        {
            log?.Invoke($"QR focus: fallback screen point is no longer inside configured window: {screenPoint.X},{screenPoint.Y}.");
            return false;
        }

        return true;
    }

    private static bool TryFocusEditableChildAtPoint(IntPtr rootWindowHandle, NativePoint screenPoint, Action<string>? log)
    {
        var candidates = EnumerateEditableChildWindows(rootWindowHandle)
            .Select(child => new
            {
                Child = child,
                HasRect = GetWindowRect(child.Handle, out var rect),
                Rect = rect
            })
            .Where(candidate =>
                candidate.HasRect &&
                screenPoint.X >= candidate.Rect.Left &&
                screenPoint.Y >= candidate.Rect.Top &&
                screenPoint.X < candidate.Rect.Right &&
                screenPoint.Y < candidate.Rect.Bottom)
            .OrderBy(candidate =>
                Math.Max(0, candidate.Rect.Right - candidate.Rect.Left) *
                Math.Max(0, candidate.Rect.Bottom - candidate.Rect.Top))
            .FirstOrDefault();

        if (candidates is null)
        {
            log?.Invoke("QR focus: no editable child found under fallback point.");
            return false;
        }

        var childPoint = screenPoint;
        if (!ScreenToClient(candidates.Child.Handle, ref childPoint))
        {
            log?.Invoke("QR focus: ScreenToClient failed for editable child under point.");
            return false;
        }

        var focused = FocusChildWindow(rootWindowHandle, candidates.Child.Handle);

        // ponytail: no movemos el cursor fisico (SetCursorPos) — con el boton aun
        // abajo Windows lo toma como arrastre y mueve la ventana externa. El foco
        // se logra con SetFocus/WM_SETFOCUS; el WM_MOUSEMOVE posteado basta.
        var lParam = MakeMouseLParam(childPoint.X, childPoint.Y);
        PostMessage(candidates.Child.Handle, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        PostMessage(candidates.Child.Handle, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);

        log?.Invoke(
            $"QR focus: focused editable child under point handle={candidates.Child.Handle}, " +
            $"class='{candidates.Child.ClassName}', point={childPoint.X},{childPoint.Y}, focused={focused}.");
        return focused;
    }

    private static bool TryPostClickToWindowAtPoint(IntPtr rootWindowHandle, NativePoint screenPoint, Action<string>? log)
    {
        var targetHandle = WindowFromPoint(screenPoint);
        if (targetHandle == IntPtr.Zero)
        {
            log?.Invoke("QR focus: WindowFromPoint failed for fallback click.");
            return false;
        }

        if (targetHandle != rootWindowHandle &&
            !IsChild(rootWindowHandle, targetHandle) &&
            GetAncestor(targetHandle, GA_ROOT) != rootWindowHandle)
        {
            log?.Invoke($"QR focus: fallback point hit another window handle={targetHandle}.");
            return false;
        }

        var targetPoint = screenPoint;
        if (!ScreenToClient(targetHandle, ref targetPoint))
        {
            log?.Invoke("QR focus: ScreenToClient failed for click target.");
            return false;
        }

        var className = GetClassName(targetHandle);
        if (!IsEditableClass(className))
        {
            log?.Invoke($"QR focus: posted click skipped because target class '{className}' is not editable.");
            return false;
        }

        FocusChildWindow(rootWindowHandle, targetHandle);
        var lParam = MakeMouseLParam(targetPoint.X, targetPoint.Y);
        var mouseMove = PostMessage(targetHandle, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        var mouseDown = PostMessage(targetHandle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        var mouseUp = PostMessage(targetHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
        log?.Invoke(
            $"QR focus: posted click target={targetHandle}, point={targetPoint.X},{targetPoint.Y}, " +
            $"move={mouseMove}, down={mouseDown}, up={mouseUp}.");

        return mouseDown && mouseUp;
    }

    private static void WaitForLeftButtonReleased(int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + Math.Max(0, timeoutMilliseconds);
        while (IsLeftMouseButtonDown() && Environment.TickCount64 < deadline)
            Thread.Sleep(15);
    }

    private static bool TryConvertScreenPointToClientPoint(
        IntPtr windowHandle,
        NativePoint screenPoint,
        out Point clientPoint)
    {
        clientPoint = Point.Empty;

        if (!PointBelongsToWindow(windowHandle, screenPoint))
            return false;

        if (!GetClientRect(windowHandle, out var clientRect))
            return false;

        var client = screenPoint;
        if (!ScreenToClient(windowHandle, ref client))
            return false;

        if (client.X < clientRect.Left ||
            client.Y < clientRect.Top ||
            client.X >= clientRect.Right ||
            client.Y >= clientRect.Bottom)
        {
            return false;
        }

        clientPoint = new Point(client.X, client.Y);
        return true;
    }

    private static bool PointBelongsToWindow(IntPtr windowHandle, NativePoint screenPoint)
    {
        var hit = WindowFromPoint(screenPoint);
        if (hit != IntPtr.Zero)
        {
            if (hit == windowHandle ||
                IsChild(windowHandle, hit) ||
                GetAncestor(hit, GA_ROOT) == windowHandle)
            {
                return true;
            }
        }

        return GetWindowRect(windowHandle, out var rect) &&
               screenPoint.X >= rect.Left &&
               screenPoint.Y >= rect.Top &&
               screenPoint.X < rect.Right &&
               screenPoint.Y < rect.Bottom;
    }

    private static IntPtr FindRootWindowAtPoint(NativePoint screenPoint)
    {
        var hit = WindowFromPoint(screenPoint);
        if (hit == IntPtr.Zero)
            return IntPtr.Zero;

        var root = GetAncestor(hit, GA_ROOT);
        return root == IntPtr.Zero ? hit : root;
    }

    private static bool TryGetWindowSnapshot(IntPtr windowHandle, out InputFocusWindowSnapshot window)
    {
        window = new InputFocusWindowSnapshot(IntPtr.Zero, "", "");

        GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0)
            return false;

        var processName = GetProcessName(processId);
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var titleLength = GetWindowTextLength(windowHandle);
        var title = titleLength > 0 ? GetWindowTitle(windowHandle, titleLength) : "";
        window = new InputFocusWindowSnapshot(windowHandle, processName, title);
        return true;
    }

    private static bool TargetMatches(EditableChildWindow child, QrInputTarget target, int index)
    {
        if (target.IsEmpty)
            return index == 0;

        if (target.NativeWindowHandle != 0 && ToStableIntHandle(child.Handle) == target.NativeWindowHandle)
            return true;

        return index == target.ControlIndex &&
               SoftEquals(child.Text, target.Name) &&
               SoftEquals(child.ClassName, target.ClassName);
    }

    private static bool SoftEquals(string? actual, string? expected)
        => string.IsNullOrWhiteSpace(expected) ||
           string.Equals(actual?.Trim() ?? "", expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IntPtr MakeMouseLParam(int x, int y)
        => new(unchecked((int)(((uint)(ushort)y << 16) | (ushort)x)));

    private static InputFocusWindowInfo? FindFirstWindow(
        string processName,
        string windowTitle,
        WindowTitleMatchMode matchMode)
    {
        foreach (var window in EnumerateTopLevelWindows())
        {
            if (WindowMatches(window, processName, windowTitle, matchMode))
                return window;
        }

        return null;
    }

    private static bool WindowMatches(
        InputFocusWindowInfo window,
        string processName,
        string windowTitle,
        WindowTitleMatchMode matchMode)
    {
        if (!string.IsNullOrWhiteSpace(processName) &&
            !string.Equals(NormalizeProcessName(processName), NormalizeProcessName(window.ProcessName),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(windowTitle))
            return true;

        return matchMode switch
        {
            WindowTitleMatchMode.Contains => window.Title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase),
            WindowTitleMatchMode.StartsWith => window.Title.StartsWith(windowTitle, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(window.Title, windowTitle, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static IEnumerable<InputFocusWindowInfo> EnumerateTopLevelWindows()
    {
        var windows = new List<InputFocusWindowInfo>();
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

            windows.Add(new InputFocusWindowInfo(hwnd, processName, title));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static List<EditableChildWindow> EnumerateEditableChildWindows(IntPtr parent)
    {
        var children = new List<EditableChildWindow>();
        EnumChildWindows(parent, (hwnd, _) =>
        {
            var className = GetClassName(hwnd);
            if (!IsEditableClass(className))
                return true;

            var length = GetWindowTextLength(hwnd);
            var text = length > 0 ? GetWindowTitle(hwnd, length) : "";
            children.Add(new EditableChildWindow(hwnd, className, text));
            return true;
        }, IntPtr.Zero);

        return children;
    }

    private static bool IsEditableClass(string className)
        => className.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
           className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase) ||
           className.Contains("TextBox", StringComparison.OrdinalIgnoreCase);

    private static bool BringWindowToFront(IntPtr windowHandle)
    {
        // ponytail: solo restaurar si esta minimizada. SW_RESTORE sobre una ventana
        // maximizada la saca de maximizado y la "mueve"/encoge — justo lo que pasaba.
        if (IsIconic(windowHandle))
            ShowWindow(windowHandle, SW_RESTORE);
        return SetForegroundWindow(windowHandle);
    }

    private static bool FocusChildWindow(IntPtr parentHandle, IntPtr childHandle)
    {
        if (childHandle == IntPtr.Zero) return false;

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(childHandle, out _);
        var attached = targetThread != 0 && AttachThreadInput(currentThread, targetThread, true);
        try
        {
            if (IsIconic(parentHandle))
                ShowWindow(parentHandle, SW_RESTORE);
            SetForegroundWindow(parentHandle);
            SetFocus(childHandle);
            PostMessage(childHandle, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private static string NormalizeProcessName(string value)
        => Path.GetFileNameWithoutExtension(value.Trim());

    private static string GetWindowTitle(IntPtr hwnd, int titleLength)
    {
        var builder = new StringBuilder(titleLength + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hwnd, builder, builder.Capacity);
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

    private static int ToStableIntHandle(IntPtr handle)
        => unchecked((int)handle.ToInt64());

    private readonly record struct InputFocusWindowInfo(IntPtr Handle, string ProcessName, string Title);
    private readonly record struct EditableChildWindow(IntPtr Handle, string ClassName, string Text);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int SW_RESTORE = 9;
    private const int WM_SETFOCUS = 0x0007;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const uint GA_ROOT = 2;
    private const int VK_LBUTTON = 0x01;
    private const int MK_LBUTTON = 0x0001;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}

public sealed record InputFocusWindowSnapshot(IntPtr WindowHandle, string ProcessName, string WindowTitle)
{
    public override string ToString() => $"{ProcessName}.exe - {WindowTitle}";
}

public sealed record InputFocusPointCapture(InputFocusWindowSnapshot Window, Point ClientPoint);

public sealed record QrInputCandidate(
    string Source,
    string AutomationId,
    string Name,
    string ClassName,
    string ControlType,
    int ControlIndex,
    int NativeWindowHandle)
{
    public QrInputTarget ToTarget()
        => new()
        {
            AutomationId = AutomationId,
            Name = Name,
            ClassName = ClassName,
            ControlType = ControlType,
            ControlIndex = ControlIndex,
            NativeWindowHandle = NativeWindowHandle
        };

    public override string ToString()
    {
        var label = !string.IsNullOrWhiteSpace(Name) ? Name : ClassName;
        if (string.IsNullOrWhiteSpace(label))
            label = ControlType;

        return $"{Source} #{ControlIndex} - {label}";
    }
}
