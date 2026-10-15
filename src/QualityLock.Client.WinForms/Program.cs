using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using QualityLock.Client.WinForms.Forms;
using QualityLock.Client.WinForms.Services;
using QualityLock.Shared.Constants;
using System.Security.Principal;

namespace QualityLock.Client.WinForms;

static class Program
{
    private const string InstanceMutexName = @"Local\QualityLockClient";

    [STAThread]
    static void Main(string[] args)
    {
        // Primera linea del log = prueba de que el runtime .NET llego a codigo administrado.
        // Si startup.log no existe tras lanzar el EXE, el host nunca arranco (en Windows 7:
        // faltan los prerequisitos de .NET 6, ver deploy\README-DESPLIEGUE.md).
        WriteStartupLog($"Arranque. SO={Environment.OSVersion} args=[{string.Join(" ", args)}]");
        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            // Sin esto, cualquier excepcion antes de Application.Run mata el proceso sin
            // pintar nada: "el instalador termina bien pero la app nunca abre".
            WriteStartupLog("Fallo no controlado en el arranque: " + ex);
            MessageBox.Show(
                "QualityLock no pudo iniciar.\n\n" + ex.Message +
                "\n\nDetalle en " + Path.Combine(AppConstants.LogsFolder, "startup.log"),
                "QualityLock — Error de arranque",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    static void Run(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (!IsRunningAsAdministrator())
        {
            WriteStartupLog("Inicio rechazado: el proceso no tiene permisos de administrador.");
            MessageBox.Show(
                "QualityLock debe ejecutarse con permisos de administrador.\n\n" +
                "Reinstale la estación para reparar la tarea de inicio automático.",
                "QualityLock — Permisos insuficientes",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        WriteStartupLog("Inicio elevado confirmado.");

        using var instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            WriteStartupLog("Salida: ya hay una instancia viva (mutex tomado).");
            // Con --setup el usuario espera una ventana: salir en silencio se ve como
            // "el acceso directo Configurar no hace nada".
            if (args.Contains("--setup", StringComparer.OrdinalIgnoreCase))
                MessageBox.Show(
                    "QualityLock ya se está ejecutando.\n\n" +
                    "Ciérrelo (o detenga la tarea QualityLockClient) antes de configurar.",
                    "QualityLock — Ya en ejecución",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            return;
        }

        Directory.CreateDirectory(AppConstants.LocalDataPath);
        Directory.CreateDirectory(AppConstants.LogsFolder);

        // Always restore Task Manager on startup — clears any stale key left
        // behind if the previous session crashed before unlocking.
        try
        {
            const string regPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
            using var key = Registry.CurrentUser.OpenSubKey(regPath, writable: true);
            key?.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
        }
        catch { /* ignore */ }

        var configPath = AppConstants.ClientConfigFile;
        var config = BuildConfiguration();

        var stationCode  = config["StationCode"] ?? string.Empty;
        var line         = config["Linea"] ?? string.Empty;
        var apiBaseUrl   = config["ApiBaseUrl"]   ?? "http://localhost:5080/";
        var hmacSecret   = config["BypassHmacSecret"] ?? "CHANGE-THIS-SECRET-IN-PRODUCTION";
        var clientApiKey = config["ClientApiKey"]
                         ?? Environment.GetEnvironmentVariable("QUALITYLOCK_CLIENT_API_KEY")
                         ?? string.Empty;

        // Segundos de inactividad antes del auto-bloqueo. Configurable por estacion
        // (AutoLockSeconds en appsettings.json). 0 = bloqueo por inactividad DESACTIVADO
        // (ej. estaciones Vision). Ausente/invalido (sin la clave) = valor por defecto.
        var autoLockSeconds = ParseAutoLock(config["AutoLockSeconds"]);

        // Anti-tecleo: si RequireScan=true, el desbloqueo directo solo se permite por
        // escaner (entrada rapida). ScanMaxAvgKeyMs = ms promedio maximo entre teclas
        // para considerarlo escaner (default 40).
        var requireScan = !string.Equals(config["RequireScan"], "false", StringComparison.OrdinalIgnoreCase);
        var scanMaxAvgKeyMs = int.TryParse(config["ScanMaxAvgKeyMs"], out var ms) && ms > 0 ? ms : 40;
        // Modo Free: estacion sin personal (ej. Vision). No hay pantalla de bloqueo ni login;
        // solo corre el guard de ventanas para cronometrar el tiempo de ajuste.
        var freeMode = string.Equals(config["StationMode"], "Free", StringComparison.OrdinalIgnoreCase);
        var windowGuardOptions = WindowAccessGuardOptions.FromConfiguration(config);
        var qrInputFocusOptions = QrInputFocusOptions.FromConfiguration(config);

        // Un ApiBaseUrl mal escrito (sin "http://", una errata) hacia que new Uri lanzara
        // aqui, antes de pintar nada: la estacion moria en silencio y --setup -- que existe
        // justo para corregirlo -- moria igual. La config nunca debe poder bloquear su
        // propio editor, asi que un URL invalido cae a un default y fuerza el setup.
        var apiUrlInvalid = !TryParseApiBase(apiBaseUrl, out var apiUri);
        if (apiUrlInvalid)
            WriteStartupLog($"ApiBaseUrl invalido ('{apiBaseUrl}'): se abrira la configuracion.");

        var http = new HttpClient
        {
            BaseAddress = apiUri,
            Timeout     = TimeSpan.FromSeconds(5)
        };
        var api        = new ApiClientService(http, stationCode, clientApiKey) { Line = line };
        var localState = new LocalStateService();
        var bypass     = new BypassService(hmacSecret);
        var adminPin   = new AdminPinService(api, config["AdminPin"]);
        var safeMode   = new SafeModeService(localState);
        safeMode.Initialize();

        // Treat this launch as a potential crash up front. If the lock screen stays
        // alive (see LockForm.MarkStartupHealthy), the counter is cleared; repeated
        // crash-on-startup within the window flips the client into safe mode.
        safeMode.BeginStartup();

        // Show setup panel when:
        //   • --setup argument is present, OR
        //   • StationCode is not yet configured
        //   • ApiBaseUrl invalido (si no, la estacion no tiene como arreglarlo)
        bool needSetup = args.Contains("--setup", StringComparer.OrdinalIgnoreCase)
                      || string.IsNullOrWhiteSpace(stationCode)
                      || apiUrlInvalid;

        if (needSetup)
        {
            var setup = new StationSetupForm(api, localState, adminPin, configPath);
            var result = setup.ShowDialog();

            if (result != DialogResult.OK)
                return;   // user closed or stopped service — exit

            // Re-read config after setup form may have saved new values
            config = BuildConfiguration();

            stationCode  = config["StationCode"] ?? string.Empty;
            apiBaseUrl   = config["ApiBaseUrl"]  ?? apiBaseUrl;
            hmacSecret   = config["BypassHmacSecret"] ?? hmacSecret;
            line         = config["Linea"] ?? line;
            clientApiKey = config["ClientApiKey"]
                         ?? Environment.GetEnvironmentVariable("QUALITYLOCK_CLIENT_API_KEY")
                         ?? clientApiKey;
            autoLockSeconds = ParseAutoLock(config["AutoLockSeconds"]);
            requireScan = !string.Equals(config["RequireScan"], "false", StringComparison.OrdinalIgnoreCase);
            scanMaxAvgKeyMs = int.TryParse(config["ScanMaxAvgKeyMs"], out var ms2) && ms2 > 0 ? ms2 : scanMaxAvgKeyMs;
            freeMode = string.Equals(config["StationMode"], "Free", StringComparison.OrdinalIgnoreCase);
            windowGuardOptions = WindowAccessGuardOptions.FromConfiguration(config);
            qrInputFocusOptions = QrInputFocusOptions.FromConfiguration(config);

            // Rebuild HttpClient with potentially new base URL
            TryParseApiBase(apiBaseUrl, out apiUri);
            http = new HttpClient
            {
                BaseAddress = apiUri,
                Timeout     = TimeSpan.FromSeconds(5)
            };
            api      = new ApiClientService(http, stationCode, clientApiKey) { Line = line };
            bypass   = new BypassService(hmacSecret);
            adminPin = new AdminPinService(api, config["AdminPin"]);
        }

        if (string.IsNullOrWhiteSpace(stationCode))
        {
            MessageBox.Show(
                "No se ha configurado el código de estación.\n" +
                "Ejecute la aplicación con el argumento --setup para configurarla.",
                "QualityLock — Error de configuración",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var operatorCache = new OperatorCacheService(api, localState, stationCode);

        // Refresca la caché de usuarios al arrancar (best-effort, 5 s timeout). Luego se
        // refresca periódicamente desde el heartbeat (ver LockForm).
        Task.Run(() => operatorCache.RefreshNowAsync()).Wait(TimeSpan.FromSeconds(5));

        var offlineSync = new OfflineSyncService(api, localState);

        var lockForm = new LockForm(stationCode, api, localState, bypass, safeMode, adminPin,
            offlineSync, operatorCache, autoLockSeconds, scanMaxAvgKeyMs, requireScan,
            windowGuardOptions, qrInputFocusOptions, freeMode);

        // --diag: arranca en modo diagnostico de escaner (calibracion del umbral).
        if (args.Contains("--diag", StringComparer.OrdinalIgnoreCase))
            lockForm.Shown += (_, _) => lockForm.EnableScanDiagnostics();

        Application.Run(lockForm);
    }

    /// <summary>
    /// true si <paramref name="url"/> sirve como BaseAddress. Si no, <paramref name="uri"/>
    /// queda en un default usable para que la pantalla de configuracion pueda abrir igual.
    /// </summary>
    private static bool TryParseApiBase(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        uri = new Uri("http://localhost:5080/");
        return false;
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void WriteStartupLog(string message)
    {
        try
        {
            Directory.CreateDirectory(AppConstants.LogsFolder);
            var path = Path.Combine(AppConstants.LogsFolder, "startup.log");
            File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnóstico best-effort: nunca impedir el arranque por un fallo de log.
        }
    }

    // 0 = desactivado (no auto-bloquea). Valor presente >=0 se respeta tal cual.
    // Clave ausente o no numerica -> valor por defecto.
    private static int ParseAutoLock(string? value)
        => int.TryParse(value, out var s) && s >= 0 ? s : AppConstants.AutoLockInactivitySeconds;

    private static IConfigurationRoot BuildConfiguration()
    {
        var exeConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        var builder = new ConfigurationBuilder();
        if (File.Exists(exeConfigPath))
            builder.AddJsonFile(exeConfigPath, optional: true);

        if (File.Exists(AppConstants.ClientConfigFile))
            builder.AddJsonFile(AppConstants.ClientConfigFile, optional: true);

        return builder.Build();
    }
}
