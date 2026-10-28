using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using QualityLock.Shared.Constants;
using QualityLock.Shared.Input;

namespace QualityLock.Client.WinForms.Services;

public sealed class SerialScannerOptions
{
    public bool Enabled { get; init; }
    public string PortName { get; init; } = "";
    public int BaudRate { get; init; } = 9600;
    // 0 = todo el codigo en un solo SendInput (lo mas rapido).
    public int KeyDelayMilliseconds { get; init; }
    public bool SendEnter { get; init; } = true;
    // Escaneres sin sufijo CR/LF: silencio que cierra el codigo.
    public int EndOfCodeSilenceMilliseconds { get; init; } = 30;
    // Antes de teclear, el MES debe confirmar que el codigo es un EBR con Vision OK.
    public bool RequireVisionOk { get; init; }

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(PortName);

    public static SerialScannerOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("SerialScanner");
        if (!section.Exists())
            return new SerialScannerOptions();

        return new SerialScannerOptions
        {
            Enabled = string.Equals(section["Enabled"], "true", StringComparison.OrdinalIgnoreCase),
            PortName = section["PortName"]?.Trim() ?? "",
            BaudRate = ConfigParse.PositiveInt(section["BaudRate"], 9600),
            KeyDelayMilliseconds = Math.Min(50, ConfigParse.NonNegativeInt(section["KeyDelayMilliseconds"], 0)),
            SendEnter = !string.Equals(section["SendEnter"], "false", StringComparison.OrdinalIgnoreCase),
            EndOfCodeSilenceMilliseconds = Math.Clamp(ConfigParse.PositiveInt(section["EndOfCodeSilenceMilliseconds"], 30), 10, 1000),
            RequireVisionOk = string.Equals(section["RequireVisionOk"], "true", StringComparison.OrdinalIgnoreCase)
        };
    }
}

/// <summary>
/// Escaner en modo serie (USB-COM / RS-232) que se comporta como uno HID: lee cada codigo
/// del puerto y lo entrega por <see cref="CodeReceived"/>; <see cref="Type"/> lo teclea con
/// SendInput donde este el cursor. Si se desconecta el cable, reconecta solo.
/// </summary>
public sealed class SerialScannerService : IDisposable
{
    private const int ReconnectMilliseconds = 1500;
    private const int MaxLogLines = 200;

    private readonly object _gate = new();
    private readonly object _deliver = new();
    private readonly List<string> _log = [];
    private readonly StringBuilder _rx = new();
    private readonly System.Threading.Timer _silenceTimer;
    private SerialScannerOptions _options;
    private System.Threading.Timer? _timer;
    private SerialPort? _port;
    private volatile bool _readFailed;
    private string? _lastOpenError;

    public SerialScannerService(SerialScannerOptions options)
    {
        _options = options;
        _silenceTimer = new System.Threading.Timer(_ => FlushOnSilence(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Un codigo completo. Se dispara en el hilo del puerto serie, uno a la vez.</summary>
    public event Action<string>? CodeReceived;

    /// <summary>Linea nueva del log, ya con hora. Se dispara en cualquier hilo.</summary>
    public event Action<string>? LogAdded;

    public string Status { get; private set; } = "Deshabilitado";

    public SerialScannerOptions Options => _options;

    /// <summary>PCBs escaneadas en esta sesion del puerto (se reinicia cada vez que conecta).</summary>
    public ScanRepeatTracker Session { get; } = new();

    public IReadOnlyList<string> RecentLog
    {
        get { lock (_log) return _log.ToArray(); }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (!_options.IsConfigured)
            {
                Status = "Deshabilitado";
                // Sin log si esta apagado: no llenar de archivos las estaciones que no lo usan.
                if (_options.Enabled) Log("Habilitado, pero sin puerto configurado.");
                return;
            }

            _lastOpenError = null;
            _timer ??= new System.Threading.Timer(_ => EnsureConnected(), null, 0, ReconnectMilliseconds);
        }
    }

    public void Restart(SerialScannerOptions options)
    {
        lock (_gate)
        {
            Stop();
            _options = options;
        }
        Start();
    }

    public void Dispose()
    {
        lock (_gate) Stop();
        _silenceTimer.Dispose();
    }

    // Llamar con _gate tomado.
    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        if (_port is not null)
        {
            ClosePort();
            Log("Detenido.");
        }
        Status = "Deshabilitado";
    }

    private void EnsureConnected()
    {
        lock (_gate)
        {
            if (_timer is null) return;   // se detuvo mientras este tick esperaba el lock

            if (_port is not null)
            {
                if (!_readFailed && _port.IsOpen && PortExists(_port.PortName)) return;
                ClosePort();
                Status = "Desconectado";
                Log($"Puerto {_options.PortName} desconectado.");
            }

            SerialPort? port = null;
            try
            {
                port = new SerialPort(_options.PortName, _options.BaudRate, Parity.None, 8, StopBits.One);
                port.DataReceived += OnData;
                port.Open();
                _port = port;
                _readFailed = false;
                lock (_rx) _rx.Clear();
                Session.Reset();   // sesion nueva del escaner: las repeticiones se cuentan de cero
                _lastOpenError = null;
                Status = $"Conectado a {_options.PortName} @ {_options.BaudRate}";
                Log(Status + ".");
            }
            catch (Exception ex)
            {
                try { port?.Dispose(); } catch { /* nada que liberar */ }
                Status = "Desconectado";
                // Reintenta cada 1.5 s: solo registra cuando el error cambia.
                if (ex.Message != _lastOpenError)
                {
                    _lastOpenError = ex.Message;
                    Log($"No se pudo abrir {_options.PortName}: {ex.Message}");
                }
            }
        }
    }

    // Llamar con _gate tomado.
    private void ClosePort()
    {
        var port = _port;
        _port = null;
        if (port is null) return;

        // Un codigo a medias de un puerto que se cayo no se teclea.
        _silenceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        lock (_rx) _rx.Clear();
        try
        {
            port.DataReceived -= OnData;
            port.Dispose();
        }
        catch { /* el cable ya no esta: cerrar puede fallar */ }
    }

    private static bool PortExists(string name)
        => SerialPort.GetPortNames().Contains(name, StringComparer.OrdinalIgnoreCase);

    // Hilo del puerto serie. No toma _gate: Close() espera a que este handler termine.
    private void OnData(object sender, SerialDataReceivedEventArgs e)
    {
        List<string> codes;
        lock (_rx)
        {
            try
            {
                codes = ScanCodeSplitter.Take(_rx, ((SerialPort)sender).ReadExisting());
            }
            catch (Exception ex)
            {
                _readFailed = true;   // el timer lo cierra y reconecta
                Log("Error de lectura: " + ex.Message);
                return;
            }

            // ponytail: escaneres sin sufijo CR/LF (p.ej. Honeywell de fabrica): si el puerto queda
            // callado EndOfCodeSilenceMilliseconds con algo en el buffer, eso es un codigo completo.
            // Techo: codigos que llegan pegados en una sola rafaga (escaneados con el puerto
            // cerrado) salen como uno solo; con el escaner programado con sufijo CR no pasa.
            _silenceTimer.Change(_rx.Length > 0 ? _options.EndOfCodeSilenceMilliseconds : Timeout.Infinite, Timeout.Infinite);
        }

        Deliver(codes);
    }

    // Hilo del timer: el escaner dejo de mandar bytes sin enviar CR/LF.
    private void FlushOnSilence()
    {
        string code;
        lock (_rx)
        {
            code = _rx.ToString().Trim();
            _rx.Clear();
        }
        if (code.Length > 0)
            Deliver([code]);
    }

    // Un codigo a la vez: el hilo del puerto y el del timer nunca teclean intercalados.
    private void Deliver(List<string> codes)
    {
        lock (_deliver)
        {
            foreach (var code in codes)
            {
                try { CodeReceived?.Invoke(code); }
                catch (Exception ex) { Log("Error al procesar el código: " + ex.Message); }
            }
        }
    }

    /// <summary>
    /// Teclea el codigo donde este el cursor, igual que un escaner HID: un caracter Unicode
    /// por evento y Enter al final. Sin retardo va todo en un solo SendInput (instantaneo y
    /// atomico: no se intercala con el teclado fisico). No mueve el foco.
    /// </summary>
    public void Type(string code)
    {
        var options = _options;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int error;

        if (options.KeyDelayMilliseconds == 0)
        {
            var inputs = new List<INPUT>(code.Length * 2 + 2);
            foreach (var c in code)
                AddKey(inputs, 0, c, KEYEVENTF_UNICODE);
            if (options.SendEnter)
                AddKey(inputs, VK_RETURN, '\0', 0);
            if (!Send(inputs.ToArray(), out error))
            {
                Log($"ERROR al teclear (SendInput, Win32 {error}): {code}");
                return;
            }
        }
        else
        {
            foreach (var c in code)
            {
                if (!SendKey(0, c, KEYEVENTF_UNICODE, out error))
                {
                    Log($"ERROR al teclear (SendInput, Win32 {error}): {code}");
                    return;
                }
                Pause(options.KeyDelayMilliseconds);
            }

            if (options.SendEnter && !SendKey(VK_RETURN, '\0', 0, out error))
            {
                Log($"ERROR al teclear Enter (SendInput, Win32 {error}): {code}");
                return;
            }
        }

        Log($"Enviado: {code} ({sw.ElapsedMilliseconds} ms)");
    }

    // Thread.Sleep(2) duerme ~15 ms (resolucion del reloj de Windows): 40 letras = 600 ms.
    // Los retardos cortos se esperan con Stopwatch para que 2 ms sean 2 ms.
    private static void Pause(int milliseconds)
    {
        if (milliseconds >= 15)
        {
            Thread.Sleep(milliseconds);
            return;
        }

        var until = System.Diagnostics.Stopwatch.GetTimestamp() +
                    milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000;
        while (System.Diagnostics.Stopwatch.GetTimestamp() < until)
            Thread.SpinWait(20);
    }

    public void Log(string message)
    {
        var now = DateTime.Now;
        var line = $"{now:HH:mm:ss}  {message}";
        lock (_log)
        {
            _log.Add(line);
            if (_log.Count > MaxLogLines) _log.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(AppConstants.LogsFolder);
                File.AppendAllText(Path.Combine(AppConstants.LogsFolder, "serial-scanner.log"),
                    $"{now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostico best-effort.
            }
        }
        LogAdded?.Invoke(line);
    }

    private static bool SendKey(ushort vk, char ch, uint flags, out int error)
    {
        var inputs = new List<INPUT>(2);
        AddKey(inputs, vk, ch, flags);
        return Send(inputs.ToArray(), out error);
    }

    // Bajar + subir la tecla.
    private static void AddKey(List<INPUT> inputs, ushort vk, char ch, uint flags)
    {
        var down = new INPUT { type = INPUT_KEYBOARD };
        down.U.ki = new KEYBDINPUT { wVk = vk, wScan = ch, dwFlags = flags };
        var up = down;
        up.U.ki.dwFlags = flags | KEYEVENTF_KEYUP;
        inputs.Add(down);
        inputs.Add(up);
    }

    private static bool Send(INPUT[] inputs, out int error)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        error = sent == inputs.Length ? 0 : Marshal.GetLastWin32Error();
        return sent == inputs.Length;
    }

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN = 0x0D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    // MOUSEINPUT es el miembro mas grande de la union: con el, sizeof(INPUT) = 28 (x86) / 40 (x64).
    // Si no coincide, SendInput devuelve 0 y no teclea nada.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
