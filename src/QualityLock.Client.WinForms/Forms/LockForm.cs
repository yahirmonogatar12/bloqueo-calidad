using Microsoft.Win32;
using QualityLock.Client.WinForms.Services;
using QualityLock.Shared.Constants;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;
using QualityLock.Shared.Input;

namespace QualityLock.Client.WinForms.Forms;

public partial class LockForm : Form
{
    // ── services ──────────────────────────────────────────────
    private readonly string _stationCode;
    private readonly ApiClientService _api;
    private readonly LocalStateService _localState;
    private readonly BypassService _bypass;
    private readonly SafeModeService _safeMode;
    private readonly AdminPinService _adminPin;
    private readonly OfflineSyncService _offlineSync;
    private readonly OperatorCacheService _operatorCache;
    private readonly int _autoLockSeconds;
    private readonly ScanSpeedDetector _scanDetector;
    private readonly bool _requireScan;
    private readonly KeyboardHookService _keyHook;

    // ── timers ────────────────────────────────────────────────
    private System.Windows.Forms.Timer _clockTimer = null!;
    private System.Windows.Forms.Timer _autoLockTimer = null!;
    private System.Windows.Forms.Timer _heartbeatTimer = null!;
    private System.Windows.Forms.Timer _guardTimer = null!;

    // ── state ─────────────────────────────────────────────────
    private bool _isLocked = true;
    private bool _processing = false;
    // True mientras hay un dialogo modal abierto (ej. pedir contrasena): suspende el
    // guard de foco/topmost para que el operador pueda escribir en el dialogo.
    private bool _modalOpen = false;
    // Modo diagnostico de escaner: muestra la velocidad medida tras cada escaneo,
    // para calibrar ScanMaxAvgKeyMs. Se activa desde el menu de la bandeja.
    private bool _scanDiagnostics = false;
    private Guid? _currentSessionId;
    private bool _currentSessionOnline;
    private string? _currentBadge;
    private string? _currentDisplayName;
    private DateTime _lastActivity = DateTime.UtcNow;
    private const string ClientVersion = "1.0.0";

    // ── controls ─────────────────────────────────────────────
    private Label _lblClock = null!;
    private Label _lblStation = null!;
    private Label _lblStatus = null!;
    private TextBox _txtBadge = null!;

    // ── tray ──────────────────────────────────────────────────
    private NotifyIcon _trayIcon = null!;

    // ── indicador de sesion activa (usuario + rol, persistente y movible) ──
    private SessionBadgeForm? _sessionBadge;

    public LockForm(string stationCode, ApiClientService api, LocalStateService localState,
        BypassService bypass, SafeModeService safeMode, AdminPinService adminPin,
        OfflineSyncService offlineSync, OperatorCacheService operatorCache,
        int autoLockSeconds, int scanMaxAvgKeyMs, bool requireScan)
    {
        _stationCode = stationCode;
        _api = api;
        _localState = localState;
        _bypass = bypass;
        _safeMode = safeMode;
        _adminPin = adminPin;
        _offlineSync = offlineSync;
        _operatorCache = operatorCache;
        _autoLockSeconds = autoLockSeconds > 0 ? autoLockSeconds : AppConstants.AutoLockInactivitySeconds;
        _scanDetector = new ScanSpeedDetector(scanMaxAvgKeyMs);
        _requireScan = requireScan;
        _keyHook = new KeyboardHookService();

        BuildUI();
        SetupTimers();
        SetupTrayIcon();
    }

    // ─────────────────────────────────────────────────────────
    // UI
    // ─────────────────────────────────────────────────────────

    private void BuildUI()
    {
        SuspendLayout();

        Text = "QualityLock";
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(16, 24, 20);   // verde-oscuro, combina con la marca
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        Location = Point.Empty;
        Icon = Branding.AppIcon();

        var safeMode = _safeMode.IsSafeMode;
        if (safeMode) BackColor = Color.FromArgb(60, 10, 10);

        // ── Contenedor centrado vertical/horizontalmente ────────
        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 6
        };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 30)); // espacio superior
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); // logo
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); // reloj
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // estacion
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // status
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 70));   // espacio inferior

        // ── Logo ────────────────────────────────────────────────
        var logo = new PictureBox
        {
            Image = Branding.Logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.Transparent
        };

        // ── Reloj ───────────────────────────────────────────────
        _lblClock = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 64, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };

        // ── Nombre de estacion ──────────────────────────────────
        _lblStation = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 16),
            ForeColor = Color.FromArgb(120, 220, 150),   // verde claro
            BackColor = Color.Transparent,
            Text = $"Estación:  {_stationCode}"
        };
        if (safeMode)
        {
            _lblStation.Text += "   ⚠ MODO SEGURO";
            _lblStation.ForeColor = Color.Gold;
        }

        // ── Instruccion / estado ────────────────────────────────
        _lblStatus = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 170, 90),
            BackColor = Color.Transparent,
            Text = "🔒  BLOQUEADO  —  Escanee su gafete"
        };

        center.Controls.Add(new Label { BackColor = Color.Transparent }, 0, 0); // spacer
        center.Controls.Add(logo, 0, 1);
        center.Controls.Add(_lblClock, 0, 2);
        center.Controls.Add(_lblStation, 0, 3);
        center.Controls.Add(_lblStatus, 0, 4);
        center.Controls.Add(new Label { BackColor = Color.Transparent }, 0, 5); // spacer

        // ── Hidden textbox captures scanner (keyboard wedge) ────
        _txtBadge = new TextBox
        {
            Multiline = false,
            BorderStyle = BorderStyle.None,
            BackColor = BackColor,
            ForeColor = BackColor,
            Font = new Font("Segoe UI", 1),
            Width = 1,
            Height = 1,
            Location = new Point(0, 0),
            TabStop = true
        };
        _txtBadge.KeyDown += TxtBadge_KeyDown;
        // Cronometra cada caracter para distinguir escaner (rapido) de tecleo humano.
        _txtBadge.KeyPress += (_, ev) =>
        {
            // Solo caracteres imprimibles cuentan; Enter/control no.
            if (!char.IsControl(ev.KeyChar)) _scanDetector.RecordKey();
        };

        Controls.Add(center);
        Controls.Add(_txtBadge);   // encima del layout pero invisible (1x1)
        _txtBadge.BringToFront();

        KeyDown += LockForm_KeyDown;
        ResumeLayout(true);
    }

    private void SetupTimers()
    {
        // ── Clock tick ─────────────────────────────────────────
        _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clockTimer.Tick += (_, _) => _lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();

        // ── Auto-lock on inactivity ────────────────────────────
        // Revisa periodicamente la inactividad GLOBAL del sistema (mouse/teclado en
        // cualquier ventana). Bloquea solo si el operador no ha usado el equipo durante
        // AutoLockInactivitySeconds. Mientras trabaje, la estacion no se bloquea.
        _autoLockTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _autoLockTimer.Tick += (_, _) =>
        {
            if (_isLocked) return;
            var idle = SystemIdleService.GetIdleTime();
            if (idle.TotalSeconds >= _autoLockSeconds)
            {
                _autoLockTimer.Stop();
                ShowLockScreen(LockReason.Inactivity);
            }
        };

        // ── Heartbeat ──────────────────────────────────────────
        _heartbeatTimer = new System.Windows.Forms.Timer
        {
            Interval = AppConstants.HeartbeatIntervalSeconds * 1000
        };
        _heartbeatTimer.Tick += async (_, _) =>
        {
            await _api.SendHeartbeatAsync(new HeartbeatRequest(
                _stationCode, DateTime.UtcNow, ClientVersion,
                _safeMode.IsSafeMode, _lastActivity, null, Line: _api.Line));

            // Opportunistically replay any queued offline events when back online.
            await _offlineSync.FlushAsync();

            // Refresca la caché de usuarios si toca, para incluir usuarios nuevos
            // sin reiniciar la estación (no-op si el backend no responde).
            await _operatorCache.RefreshIfDueAsync();
        };
        _heartbeatTimer.Start();

        // ── Guard: enforce topmost every 300 ms while locked ──
        _guardTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _guardTimer.Tick += GuardTimer_Tick;
        _guardTimer.Start();
    }

    private void GuardTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isLocked || !IsHandleCreated || IsDisposed || !Visible) return;

        // Mientras haya un dialogo modal (contrasena, login admin), no robamos el foco:
        // el operador necesita poder escribir en el.
        if (_modalOpen) return;

        // Si el Administrador de tareas esta abierto, el admin esta haciendo una
        // recuperacion deliberada de la maquina (la app se congelo, hay que cerrar algo,
        // etc.). En vez de pelear por el Z-order con una ventana fullscreen —lo que dejaba
        // el taskmgr tapado o inusable— DESBLOQUEAMOS automaticamente y registramos el
        // evento como recuperacion auditada. Solo se dispara una vez por apertura.
        // IMPORTANTE: este chequeo va ANTES que el de modo seguro: en modo seguro la
        // recuperacion importa todavia mas (el cliente ya viene de crashes repetidos).
        if (IsRecoveryProcessRunning())
        {
            _ = RecoveryUnlockAsync();
            return;
        }

        // In safe mode, don't fight the user for the foreground — recovery must be possible.
        if (_safeMode.IsSafeMode) return;

        // Force this window back to the very top of the Z-order
        if (!TopMost) TopMost = true;
        BringToFront();
        Activate();

        // Ensure the hidden badge textbox keeps keyboard focus so the scanner works
        if (!_txtBadge.Focused)
            _txtBadge.Focus();
    }

    // Evita disparar el desbloqueo de recuperacion mas de una vez mientras el guard
    // sigue viendo el taskmgr abierto.
    private bool _recoveryUnlocking;

    /// <summary>
    /// Desbloqueo automatico de recuperacion: se invoca cuando el guard detecta el
    /// Administrador de tareas abierto. Deja la maquina usable (oculta el bloqueo) y
    /// registra el evento como recuperacion auditada (quien/cuando), sin pedir gafete.
    /// Es una via de emergencia deliberada del admin; el operador normal no abre taskmgr.
    /// </summary>
    private async Task RecoveryUnlockAsync()
    {
        if (_recoveryUnlocking || !_isLocked) return;
        _recoveryUnlocking = true;
        try
        {
            // Registra la recuperacion (best-effort, online u offline en cola).
            var correlationId = Guid.NewGuid().ToString();
            EnqueueOfflineEvent(StationEventType.ClientRecovered, "TASK-MANAGER",
                sessionId: null, correlationId,
                new { reason = "TaskManagerOpened", at = DateTime.UtcNow });

            // Desbloqueo visual: quita el hook de teclado, restaura el taskmgr y oculta
            // el bloqueo. No abrimos sesion de operador: es una recuperacion de admin.
            UnlockUi("Recuperación (Administrador de tareas)", "recovery");
        }
        catch { /* nunca dejar atascado al admin por un fallo de registro */ }
    }

    // Procesos de recuperacion del sistema ante los que la pantalla de bloqueo cede el
    // primer plano (en minusculas, sin extension).
    private static readonly string[] RecoveryProcesses = ["taskmgr"];

    // Cache breve de la deteccion: enumerar procesos cada 300 ms es innecesario, basta
    // refrescar ~1 vez por segundo. (tick, resultado)
    private DateTime _recoveryCheckedAt = DateTime.MinValue;
    private bool _recoveryRunningCache;

    /// <summary>
    /// True si hay un proceso de recuperacion (ej. el Administrador de tareas) en
    /// ejecucion con una ventana visible. Detectar por proceso —y no solo por foreground—
    /// evita la carrera en la que el guard re-afirma TopMost antes de que taskmgr alcance
    /// el primer plano, impidiendo abrirlo. Si falla la deteccion, devuelve false para no
    /// debilitar el bloqueo por error.
    /// </summary>
    private bool IsRecoveryProcessRunning()
    {
        var now = DateTime.UtcNow;
        if ((now - _recoveryCheckedAt).TotalMilliseconds < 250)
            return _recoveryRunningCache;
        _recoveryCheckedAt = now;

        try
        {
            bool running = false;
            foreach (var name in RecoveryProcesses)
            {
                // Basta con que el proceso exista. NO exigimos MainWindowHandle != 0: el
                // Administrador de tareas corre ELEVADO (integridad alta) y un proceso de
                // integridad media no puede leer el handle de su ventana, devolviendo 0.
                // Esa era la razon por la que la deteccion fallaba.
                var procs = System.Diagnostics.Process.GetProcessesByName(name);
                try
                {
                    if (procs.Length > 0) { running = true; break; }
                }
                finally
                {
                    foreach (var p in procs) p.Dispose();
                }
            }
            _recoveryRunningCache = running;
        }
        catch
        {
            _recoveryRunningCache = false;
        }

        return _recoveryRunningCache;
    }

    // ─────────────────────────────────────────────────────────
    // Tray icon
    // ─────────────────────────────────────────────────────────

    private void SetupTrayIcon()
    {
        var menu = new ContextMenuStrip();

        var itemSetup = new ToolStripMenuItem("⚙  Configuración de estación...");
        itemSetup.Click += TraySetup_Click;

        var itemLock = new ToolStripMenuItem("🔒  Bloquear ahora");
        itemLock.Click += (_, _) =>
        {
            if (!_isLocked) ShowLockScreen(LockReason.Manual);
        };

        var itemRefresh = new ToolStripMenuItem("🔄  Refrescar usuarios");
        itemRefresh.Click += TrayRefreshUsers_Click;

        var itemDiag = new ToolStripMenuItem("🧪  Diagnóstico de escáner") { CheckOnClick = true };
        itemDiag.Click += (_, _) =>
        {
            _scanDiagnostics = itemDiag.Checked;
            SetStatus(_scanDiagnostics
                ? "Diagnóstico ON: escanee/teclee para ver la velocidad medida."
                : "🔒  BLOQUEADO  —  Escanee su gafete",
                _scanDiagnostics ? Color.Cyan : Color.FromArgb(255, 170, 90));
        };

        menu.Items.Add(itemSetup);
        menu.Items.Add(itemRefresh);
        menu.Items.Add(itemDiag);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(itemLock);

        _trayIcon = new NotifyIcon
        {
            Icon          = BuildTrayIcon(),
            Text          = $"QualityLock — {_stationCode}",
            ContextMenuStrip = menu,
            Visible       = true
        };

        _trayIcon.DoubleClick += TraySetup_Click;
    }

    /// <summary>Creates a small lock icon programmatically (no external .ico needed).</summary>
    private static Icon BuildTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g   = Graphics.FromImage(bmp);

        // Background
        g.Clear(Color.FromArgb(30, 60, 120));

        // Shackle (top half of lock)
        using var shacklePen = new Pen(Color.White, 1.5f);
        g.DrawArc(shacklePen, 4, 2, 8, 7, 180, 180);

        // Body of lock
        using var bodyBrush = new SolidBrush(Color.White);
        g.FillRectangle(bodyBrush, 3, 7, 10, 7);

        // Key hole
        using var holeBrush = new SolidBrush(Color.FromArgb(30, 60, 120));
        g.FillEllipse(holeBrush, 6, 9, 4, 3);

        return Icon.FromHandle(bmp.GetHicon());
    }

    private async void TraySetup_Click(object? sender, EventArgs e)
    {
        // Ask for admin credentials (usuarios_sistema) before opening setup.
        var auth = await AdminLoginPrompt.AuthenticateAsync(_adminPin, owner: null);
        if (auth is null) return;          // cancelled
        if (!auth.Value.Authenticated)
        {
            MessageBox.Show(auth.Value.Message, "QualityLock",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var configPath = AppConstants.ClientConfigFile;
        using var setup = new StationSetupForm(_api, _localState, _adminPin, configPath);
        setup.ShowDialog();

        // Si dentro de la configuracion se pulso "Detener servicio", cerramos la sesion
        // activa (para que NO quede 'En curso') y terminamos la app.
        if (setup.StopServiceRequested)
            await StopServiceAsync();
    }

    /// <summary>
    /// Cierra la sesion activa de forma limpia y termina la aplicacion. Usado por
    /// "Detener servicio": al ser un apagado intencional, la sesion se marca cerrada en
    /// vez de quedar huerfana.
    /// </summary>
    private async Task StopServiceAsync()
    {
        if (_currentSessionId.HasValue)
        {
            try
            {
                if (_currentSessionOnline)
                    await _api.EndSessionAsync(new EndSessionRequest(
                        _currentSessionId.Value, _stationCode, "ServiceStopped",
                        DateTime.UtcNow, IsOnline: true, Line: _api.Line));
                else
                    EnqueueOfflineEvent(StationEventType.AutoLock, _currentBadge,
                        sessionId: null, Guid.NewGuid().ToString(), new { reason = "ServiceStopped" });
            }
            catch { /* salimos de todos modos */ }
        }

        // Restaura el Administrador de tareas por si quedo deshabilitado.
        SetTaskManagerEnabled(true);
        Environment.Exit(0);
    }

    /// <summary>Refresca la caché de usuarios bajo demanda desde la bandeja del sistema.</summary>
    private async void TrayRefreshUsers_Click(object? sender, EventArgs e)
    {
        var ok = await _operatorCache.RefreshNowAsync();
        var count = _localState.LoadOperatorCache().Count;
        _trayIcon.ShowBalloonTip(
            3000,
            "QualityLock",
            ok ? $"Usuarios actualizados ({count} en caché)."
               : "No se pudo actualizar (sin conexión). Se conserva la caché anterior.",
            ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    // ─────────────────────────────────────────────────────────
    // Form events
    // ─────────────────────────────────────────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // In safe mode we deliberately skip the aggressive lock so an administrator
        // can recover the machine (no keyboard hook).
        // Nota: el Administrador de tareas se deja SIEMPRE habilitado a proposito, para
        // que un admin pueda recuperar la maquina (Ctrl+Alt+Supr -> Task Manager) si la
        // app se congela. La pantalla de bloqueo sigue siendo topmost + hook de teclado.
        if (!_safeMode.IsSafeMode)
        {
            _keyHook.Install();
        }

        _txtBadge.Focus();

        // Replay any events queued while offline during a previous run.
        _ = _offlineSync.FlushAsync();

        // If the UI stays up for the grace period, the startup is considered healthy
        // and the crash counter is cleared.
        StartHealthyWatchdog();
    }

    /// <summary>
    /// One-shot timer: once the lock screen has survived a short grace period the
    /// run is marked healthy, clearing any accumulated crash-on-startup count.
    /// </summary>
    private void StartHealthyWatchdog()
    {
        var watchdog = new System.Windows.Forms.Timer { Interval = 10_000 };
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            watchdog.Dispose();
            _safeMode.MarkStartupHealthy();
        };
        watchdog.Start();
    }

    // Prevent user from closing the form via Alt+F4 or other means
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _isLocked)
        {
            e.Cancel = true;
            return;
        }
        SetTaskManagerEnabled(true);    // always restore on exit
        _keyHook.Uninstall();
        _clockTimer.Dispose();
        _heartbeatTimer.Dispose();
        _autoLockTimer.Dispose();
        _guardTimer.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        if (_sessionBadge is not null) { _sessionBadge.Dispose(); _sessionBadge = null; }
        base.OnFormClosing(e);
    }

    // ─────────────────────────────────────────────────────────
    // Badge scan
    // ─────────────────────────────────────────────────────────

    private async void TxtBadge_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;

        var badge = _txtBadge.Text.Trim();
        // Evaluamos la velocidad ANTES de limpiar/reiniciar.
        var cameFromScanner = _scanDetector.LooksLikeScan();
        var avgMs = _scanDetector.LastAvgGapMs;
        _txtBadge.Clear();
        _scanDetector.Reset();

        if (string.IsNullOrEmpty(badge) || _processing) return;

        // Modo diagnostico (calibracion de escaner): SOLO mide y muestra la velocidad,
        // sin desbloquear, para poder escanear/teclear varias veces y leer los numeros.
        if (_scanDiagnostics)
        {
            var verdict = cameFromScanner ? "ESCANER ✔" : "TECLEO ✖";
            SetStatus($"{badge.Length} chars · {avgMs:F0} ms/tecla → {verdict}", Color.Cyan);
            return;
        }

        await HandleBadgeScanAsync(badge, cameFromScanner);
    }

    private async Task HandleBadgeScanAsync(string badgeCode, bool cameFromScanner)
    {
        _processing = true;
        SetStatus("Validando…", Color.Yellow);

        try
        {
            // Entrada tecleada a mano (no escaner): nunca desbloquea directo, pasa por la
            // ruta con contrasena. Si la estacion exige escaner (_requireScan), solo un
            // admin puede entrar a mano (respaldo); si no, cualquier usuario con su pass.
            if (!cameFromScanner)
            {
                await HandleManualEntryAsync(badgeCode, adminOnly: _requireScan);
                return;
            }

            var isOnline = await _api.IsAvailableAsync();

            if (isOnline)
            {
                var result = await _api.ValidateBadgeAsync(
                    new BadgeValidationRequest(_stationCode, badgeCode, DateTime.UtcNow, Line: _api.Line));

                if (result?.Decision == ValidationDecision.Allowed)
                    await GrantAccessAsync(badgeCode, result.DisplayName, result.Role, isOnline: true);
                else
                    SetStatus($"ACCESO DENEGADO: {result?.DenyReason ?? "Sin respuesta del servidor"}", Color.OrangeRed);
            }
            else
            {
                var cache = _localState.LoadOperatorCache();
                var cached = cache.FirstOrDefault(o =>
                    string.Equals(o.BadgeCode, badgeCode, StringComparison.OrdinalIgnoreCase));

                if (cached is not null)
                    await GrantAccessAsync(badgeCode, cached.DisplayName, cached.Role, isOnline: false);
                else
                    SetStatus("OFFLINE — Operador no autorizado en caché local", Color.OrangeRed);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error inesperado: {ex.Message}", Color.OrangeRed);
        }
        finally
        {
            _processing = false;
            // Only refocus if still locked (access was denied)
            if (_isLocked)
                _txtBadge.Focus();
        }
    }

    /// <summary>
    /// Entrada manual (tecleada, no escaneada). Pide la contrasena del propio usuario.
    /// Politica segun <paramref name="adminOnly"/>:
    ///   - true  (RequireScan ON): solo un usuario ADMIN puede entrar a mano (respaldo);
    ///            un operador normal es rechazado y debe usar el escaner.
    ///   - false (RequireScan OFF): cualquier usuario con su contrasena puede entrar.
    /// </summary>
    private async Task HandleManualEntryAsync(string username, bool adminOnly)
    {
        // Vía de recuperación garantizada (online u offline): si lo tecleado es el PIN
        // local de administrador, desbloquea como admin sin tocar la red. Esto evita que
        // una estación sin conexión (o sin caché) quede atrapada: el admin de planta
        // siempre puede entrar con el PIN y llegar a la configuración.
        if (_adminPin.ValidatePin(username))
        {
            await GrantAccessAsync("ADMIN-PIN", "Administrador (PIN local)", "admin", isOnline: false);
            return;
        }

        if (!await _api.IsAvailableAsync())
        {
            SetStatus("Sin conexión: escanee su gafete, o teclee el PIN local de administrador.", Color.OrangeRed);
            return;
        }

        var password = PromptPassword(username);
        if (password is null)
        {
            SetStatus("Use el escáner QR.", Color.OrangeRed);
            return;
        }

        var login = await _api.UserLoginAsync(new AdminLoginRequest(username, password));
        if (login is not { Authenticated: true })
        {
            SetStatus("Contraseña incorrecta. Use el escáner QR.", Color.OrangeRed);
            return;
        }

        // Si la estacion exige escaner, el tecleo manual solo lo permiten los roles
        // configurados (por defecto superadmin, admin, Tecnico QA).
        if (adminOnly && !login.CanUnlockManually)
        {
            SetStatus("Use el escáner QR. El acceso manual está restringido a roles autorizados.", Color.OrangeRed);
            return;
        }

        // Desbloquea como ese usuario. La sesion se abre online via el flujo normal.
        await GrantAccessAsync(login.Username, login.DisplayName, login.Role, isOnline: true);
    }

    /// <summary>Dialogo modal con estilo para pedir la contraseña. Null si se cancela.</summary>
    private string? PromptPassword(string username)
    {
        using var dlg = new Form
        {
            Text = "Confirmar identidad",
            Width = 420, Height = 250,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, TopMost = true,
            BackColor = Color.White,
            Icon = Branding.AppIcon()
        };

        // Header con logo + banda verde
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.White };
        var logoBox = new PictureBox
        {
            Image = Branding.Logo, SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(18, 10), Size = new Size(42, 42), BackColor = Color.Transparent
        };
        var lblTitle = new Label
        {
            Text = "Confirmar identidad", Location = new Point(70, 12), AutoSize = true,
            Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Branding.GreenDark
        };
        var lblUser = new Label
        {
            Text = $"Usuario: {username}", Location = new Point(72, 38), AutoSize = true,
            Font = new Font("Segoe UI", 9), ForeColor = Color.Gray
        };
        var accent = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Branding.Green };
        header.Controls.AddRange([logoBox, lblTitle, lblUser, accent]);

        var lbl = new Label { Text = "Contraseña:", Location = new Point(20, 82), AutoSize = true, ForeColor = Branding.Ink };
        var txt = new TextBox
        {
            Location = new Point(20, 106), Width = 364, Height = 30,
            UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11),
            BorderStyle = BorderStyle.FixedSingle
        };
        var btn = new FlatRoundButton
        {
            Text = "Entrar", Location = new Point(20, 150), Width = 364, Height = 40,
            BaseColor = Branding.Green, DialogResult = DialogResult.OK
        };

        dlg.Controls.AddRange([lbl, txt, btn, header]);
        dlg.AcceptButton = btn;
        dlg.Shown += (_, _) => { dlg.Activate(); txt.Focus(); };

        // Suspende el guard de foco mientras el dialogo esta abierto, si no el
        // GuardTimer roba el foco de vuelta al campo de escaneo.
        _modalOpen = true;
        try
        {
            return dlg.ShowDialog(this) == DialogResult.OK && txt.Text.Length > 0 ? txt.Text : null;
        }
        finally
        {
            _modalOpen = false;
        }
    }

    // ─────────────────────────────────────────────────────────
    // Grant / Revoke access
    // ─────────────────────────────────────────────────────────

    private async Task GrantAccessAsync(string badgeCode, string displayName, string? role, bool isOnline)
    {
        var correlationId = Guid.NewGuid().ToString();
        _currentSessionOnline = isOnline;
        _currentBadge = badgeCode;
        _currentDisplayName = displayName;

        // Start session
        if (isOnline)
        {
            var session = await _api.StartSessionAsync(new StartSessionRequest(
                _stationCode, badgeCode, DateTime.UtcNow, isOnline, correlationId, Line: _api.Line));
            _currentSessionId = session?.SessionId;
        }
        else
        {
            _currentSessionId = Guid.NewGuid();
            // Offline sessions are local-only; the server has no matching
            // station_sessions_QA row, so queued audit events must not carry it.
            EnqueueOfflineEvent(StationEventType.UnlockGranted, badgeCode,
                sessionId: null, correlationId, null);
        }

        UnlockUi(displayName, role);
    }

    /// <summary>
    /// Performs the visual/desktop side of unlocking: drops the keyboard hook,
    /// restores Task Manager, hides the lock screen and starts the inactivity timer.
    /// Session bookkeeping is the caller's responsibility.
    /// </summary>
    private void UnlockUi(string displayName, string? role)
    {
        _isLocked = false;
        _lastActivity = DateTime.UtcNow;

        // ── Remove keyboard block (skipped in safe mode where it was never installed) ──
        _keyHook.Uninstall();

        // ── Re-enable Task Manager now that operator has access ─
        SetTaskManagerEnabled(true);

        // ── HIDE the lock screen — user gets full desktop access ──
        Hide();

        // ── Start inactivity timer ─────────────────────────────
        _autoLockTimer.Stop();
        _autoLockTimer.Start();

        // ── Indicador persistente de sesion (usuario + rol, movible) ──
        if (_sessionBadge is null)
        {
            _sessionBadge = new SessionBadgeForm();
            // El boton "Cerrar sesion" del indicador vuelve a bloquear la estacion.
            _sessionBadge.LockRequested += () => ShowLockScreen(LockReason.Manual);
        }
        _sessionBadge.ShowSession(displayName, role);
    }

    /// <summary>Re-display the fullscreen lock screen.</summary>
    private async void ShowLockScreen(LockReason reason = LockReason.None)
    {
        _autoLockTimer.Stop();

        // Recordamos quien estaba y por que se bloquea, para el mensaje en pantalla.
        var previousUser = _currentDisplayName;

        // Close open session if any
        if (_currentSessionId.HasValue)
        {
            if (_currentSessionOnline)
            {
                await _api.EndSessionAsync(new EndSessionRequest(
                    _currentSessionId.Value, _stationCode,
                    "AutoLock", DateTime.UtcNow, IsOnline: true, Line: _api.Line));
            }
            else
            {
                // Session was opened offline: queue the lock audit without the
                // local-only session id.
                EnqueueOfflineEvent(StationEventType.AutoLock, _currentBadge,
                    sessionId: null, Guid.NewGuid().ToString(),
                    new { reason = "AutoLock" });
            }

            _currentSessionId = null;
            _currentBadge = null;
            _currentDisplayName = null;
        }

        _isLocked = true;
        // Permite que una nueva apertura del Administrador de tareas vuelva a disparar el
        // desbloqueo de recuperacion.
        _recoveryUnlocking = false;

        // Ocultar el indicador de sesion: la sesion termino.
        _sessionBadge?.Hide();

        // In safe mode, keep the lock screen visible but do NOT re-arm the
        // aggressive lock — the machine must stay recoverable by an admin.
        // Task Manager se deja habilitado siempre (ver nota en OnShown).
        if (!_safeMode.IsSafeMode)
        {
            _keyHook.Install();             // re-install keyboard hook before showing
        }

        ShowLockReason(reason, previousUser);
        _txtBadge.Clear();

        Show();
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        BringToFront();
        Activate();
        _txtBadge.Focus();
    }

    /// <summary>Motivos por los que la estación se bloquea (para informar al operador).</summary>
    private enum LockReason { None, Inactivity, Manual, EndSession }

    /// <summary>
    /// Muestra en la pantalla de bloqueo el motivo del bloqueo y, si aplica, quién estaba
    /// y a qué hora se bloqueó.
    /// </summary>
    private void ShowLockReason(LockReason reason, string? previousUser)
    {
        var hora = DateTime.Now.ToString("HH:mm");
        var quien = string.IsNullOrWhiteSpace(previousUser) ? "" : $" — {previousUser}";

        var msg = reason switch
        {
            LockReason.Inactivity => $"🔒  BLOQUEADO POR INACTIVIDAD{quien}  ({hora})",
            LockReason.Manual     => $"🔒  SESIÓN CERRADA MANUALMENTE{quien}  ({hora})",
            LockReason.EndSession => $"🔒  SESIÓN FINALIZADA{quien}  ({hora})",
            _                     => "🔒  BLOQUEADO  —  Escanee su gafete"
        };
        SetStatus(msg, Color.FromArgb(255, 170, 90));
    }

    /// <summary>Manual lock from tray or any future trigger.</summary>
    public void ManualLock() => ShowLockScreen(LockReason.Manual);

    /// <summary>Activa el modo diagnostico de escaner al arrancar (flag --diag).</summary>
    public void EnableScanDiagnostics()
    {
        _scanDiagnostics = true;
        SetStatus("Diagnóstico ON: escanee/teclee para ver la velocidad medida.", Color.Cyan);
    }

    // ─────────────────────────────────────────────────────────
    // Admin hotkey  Ctrl+Shift+Alt+F12
    // ─────────────────────────────────────────────────────────

    private void LockForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.Alt && e.KeyCode == Keys.F12)
        {
            e.Handled = true;
            using var admin = new AdminPanelForm(_stationCode, _api, _bypass);
            var result = admin.ShowDialog(this);

            if (result == DialogResult.OK && admin.BypassGranted && _isLocked)
                GrantBypassAccessAsync(admin.BypassReason);
        }
    }

    /// <summary>
    /// Unlocks the station using a validated offline bypass token and records a
    /// BypassUsed audit event (queued for sync). No operator session is opened —
    /// the bypass itself is the audit trail.
    /// </summary>
    private void GrantBypassAccessAsync(string reason)
    {
        EnqueueOfflineEvent(StationEventType.BypassUsed, badgeCode: null,
            sessionId: null, Guid.NewGuid().ToString(), new { reason });

        // No session id: nothing for ShowLockScreen to close later.
        _currentSessionId = null;
        _currentBadge = null;
        _currentSessionOnline = false;

        UnlockUi("Bypass de contingencia", $"Motivo: {reason}");
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(text, color)); return; }
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    // ─────────────────────────────────────────────────────────
    // Offline event queue
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes a <see cref="StationEventRequest"/> to the local offline queue.
    /// Offline events are audit-only and intentionally omit SessionId because the
    /// server has no station_sessions_QA row for a local-only session.
    /// DetailsJson is produced by the serializer (never string interpolation) so
    /// reasons/comments with quotes cannot corrupt the audit record.
    /// </summary>
    private void EnqueueOfflineEvent(
        StationEventType type, string? badgeCode, Guid? sessionId,
        string correlationId, object? details)
    {
        var detailsJson = details is null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(details);

        var evt = new StationEventRequest(
            _stationCode, badgeCode, null, type,
            DateTime.UtcNow, detailsJson, "Client-Offline", correlationId, Line: _api.Line);

        _localState.AppendEventQueue(
            System.Text.Json.JsonSerializer.Serialize(evt,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }

    // ─────────────────────────────────────────────────────────
    // Security helpers
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Disables or re-enables Task Manager via registry so it cannot be used
    /// to kill this process while the station is locked.
    /// Requires the app to run with sufficient user rights (no elevation needed
    /// for HKCU). If it fails silently the guard timer still provides protection.
    /// </summary>
    private static void SetTaskManagerEnabled(bool enabled)
    {
        try
        {
            const string path = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
            if (enabled)
                key?.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
            else
                key?.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
        }
        catch { /* silently ignore — guard timer compensates */ }
    }
}
