using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using QualityLock.Client.WinForms.Forms;
using QualityLock.Client.WinForms.Services;
using QualityLock.Shared.Constants;

namespace QualityLock.Client.WinForms;

static class Program
{
    private const string InstanceMutexName = @"Local\QualityLockClient";

    [STAThread]
    static void Main(string[] args)
    {
        using var instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();

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
        // (AutoLockSeconds en appsettings.json); si no es valido, usa el valor por defecto.
        var autoLockSeconds = int.TryParse(config["AutoLockSeconds"], out var s) && s > 0
                            ? s
                            : AppConstants.AutoLockInactivitySeconds;

        // Anti-tecleo: si RequireScan=true, el desbloqueo directo solo se permite por
        // escaner (entrada rapida). ScanMaxAvgKeyMs = ms promedio maximo entre teclas
        // para considerarlo escaner (default 40).
        var requireScan = !string.Equals(config["RequireScan"], "false", StringComparison.OrdinalIgnoreCase);
        var scanMaxAvgKeyMs = int.TryParse(config["ScanMaxAvgKeyMs"], out var ms) && ms > 0 ? ms : 40;

        var http = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
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
        bool needSetup = args.Contains("--setup", StringComparer.OrdinalIgnoreCase)
                      || string.IsNullOrWhiteSpace(stationCode);

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
            autoLockSeconds = int.TryParse(config["AutoLockSeconds"], out var s2) && s2 > 0
                            ? s2
                            : autoLockSeconds;
            requireScan = !string.Equals(config["RequireScan"], "false", StringComparison.OrdinalIgnoreCase);
            scanMaxAvgKeyMs = int.TryParse(config["ScanMaxAvgKeyMs"], out var ms2) && ms2 > 0 ? ms2 : scanMaxAvgKeyMs;

            // Rebuild HttpClient with potentially new base URL
            http = new HttpClient
            {
                BaseAddress = new Uri(apiBaseUrl),
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

        var lockForm = new LockForm(stationCode, api, localState, bypass, safeMode, adminPin, offlineSync, operatorCache, autoLockSeconds, scanMaxAvgKeyMs, requireScan);

        // --diag: arranca en modo diagnostico de escaner (calibracion del umbral).
        if (args.Contains("--diag", StringComparer.OrdinalIgnoreCase))
            lockForm.Shown += (_, _) => lockForm.EnableScanDiagnostics();

        Application.Run(lockForm);
    }

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
