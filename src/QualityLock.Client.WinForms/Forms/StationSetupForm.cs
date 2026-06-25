using Microsoft.Extensions.Configuration;
using QualityLock.Client.WinForms.Services;
using QualityLock.Shared.Constants;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QualityLock.Client.WinForms.Forms;

/// <summary>
/// Panel de configuración y gestión de la estación.
/// Accesible desde el menú de inicio o vía argumento --setup.
/// </summary>
public class StationSetupForm : Form
{
    private readonly ApiClientService _api;
    private readonly LocalStateService _localState;
    private readonly AdminPinService _adminPin;
    private readonly string _configPath;

    /// <summary>True si el usuario pulso "Detener servicio" (autenticado). El caller debe
    /// cerrar la sesion activa, si la hay, y terminar la aplicacion.</summary>
    public bool StopServiceRequested { get; private set; }

    // ── Conexión ──────────────────────────────────────────────
    private TextBox _txtApiUrl = null!;
    private Label _lblApiStatus = null!;
    private Button _btnTestConnection = null!;

    // ── Datos de la estación ───────────────────────────────────
    private TextBox _txtStationCode = null!;
    private TextBox _txtStationName = null!;
    private ComboBox _cmbStationType = null!;
    private TextBox _txtHostName = null!;
    private CheckBox _chkActive = null!;

    // ── Seguridad / bloqueo ────────────────────────────────────
    private ComboBox _cmbStationMode = null!;
    private CheckBox _chkNoAutoLock = null!;
    private NumericUpDown _numAutoLock = null!;
    private CheckBox _chkRequireScan = null!;
    private NumericUpDown _numScanMaxMs = null!;
    private TextBox _txtAdminPin = null!;

    // ── Ventanas externas protegidas ───────────────────────────
    private CheckBox _chkWindowGuardEnabled = null!;
    private CheckBox _chkWindowGuardManualLogin = null!;
    private NumericUpDown _numWindowGuardPollMs = null!;
    private ComboBox _cmbOpenWindows = null!;
    private DataGridView _gridWindowRules = null!;
    private Button _btnRefreshWindows = null!;
    private Button _btnUseSelectedWindow = null!;
    private Button _btnAddWindowRule = null!;
    private Button _btnRemoveWindowRule = null!;

    // ── Foco inicial al input QR externo ───────────────────────
    private CheckBox _chkQrInputFocusEnabled = null!;
    private NumericUpDown _numQrInputFocusDelay = null!;
    private TextBox _txtQrInputProcessName = null!;
    private TextBox _txtQrInputWindowTitle = null!;
    private ComboBox _cmbQrInputWindows = null!;
    private ComboBox _cmbQrInputTargets = null!;
    private Button _btnRefreshQrInputWindows = null!;
    private Button _btnUseQrInputWindow = null!;
    private Button _btnRefreshQrInputTargets = null!;
    private Button _btnTestQrInputFocus = null!;
    private Button _btnCaptureQrInputPoint = null!;
    private CheckBox _chkQrInputFallbackClick = null!;
    private TextBox _txtQrInputFallbackPoint = null!;
    private QrInputCandidate? _selectedQrInputTarget;
    private Point? _qrInputFallbackPoint;

    // ── Acciones ──────────────────────────────────────────────
    private Button _btnRegister = null!;
    private Button _btnSaveConfig = null!;
    private Button _btnStopService = null!;
    private Button _btnStartService = null!;

    // ── Estado ────────────────────────────────────────────────
    private Label _lblCacheCount = null!;
    private Label _lblResult = null!;

    public StationSetupForm(ApiClientService api, LocalStateService localState,
        AdminPinService adminPin, string configPath)
    {
        _api = api;
        _localState = localState;
        _adminPin = adminPin;
        _configPath = configPath;
        BuildUI();
        LoadCurrentConfig();
    }

    // ─────────────────────────────────────────────────────────
    // Build UI
    // ─────────────────────────────────────────────────────────

    private const int CardWidth = 600;
    private const int LabelX = 16;
    private const int FieldX = 180;

    private void BuildUI()
    {
        SuspendLayout();

        Text = "QualityLock — Configuración de Estación";
        Width = 680;
        Height = 860;
        MinimumSize = new Size(680, 700);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Branding.Panel;
        Font = new Font("Segoe UI", 10);
        Icon = Branding.AppIcon();

        // ── Header con logo y banda verde ──
        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.White };
        var logoBox = new PictureBox
        {
            Image = Branding.Logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(22, 14),
            Size = new Size(56, 56)
        };
        var lblBrand = new Label
        {
            Text = "QualityLock",
            Location = new Point(90, 16),
            AutoSize = true,
            Font = new Font("Segoe UI", 19, FontStyle.Bold),
            ForeColor = Branding.GreenDark
        };
        var lblSub = new Label
        {
            Text = "Configuración de estación",
            Location = new Point(92, 52),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.Gray
        };
        var accent = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Branding.Green };
        header.Controls.AddRange([logoBox, lblBrand, lblSub, accent]);

        var scroll = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(24, 14, 24, 18)
        };
        Controls.Add(scroll);
        Controls.Add(header);

        // ── Tarjeta: Conexión API ──
        var cardApi = MakeCard("Conexión API", out var bodyApi, 96);
        bodyApi.Controls.Add(MakeLabel("URL del servidor:", LabelX, 8));
        _txtApiUrl = MakeTextBox(FieldX, 6, 280);
        _btnTestConnection = MakeButton("Probar", FieldX + 290, 5, 100, Color.FromArgb(50, 110, 200));
        _btnTestConnection.Height = 30;
        _btnTestConnection.Click += BtnTestConnection_Click;
        bodyApi.Controls.Add(_txtApiUrl);
        bodyApi.Controls.Add(_btnTestConnection);
        _lblApiStatus = new Label
        {
            AutoSize = false, Location = new Point(FieldX, 42), Size = new Size(390, 20),
            Font = new Font("Segoe UI", 9, FontStyle.Italic), ForeColor = Color.Gray, Text = "Sin verificar"
        };
        bodyApi.Controls.Add(_lblApiStatus);
        scroll.Controls.Add(cardApi);

        // ── Tarjeta: Datos de la estación ──
        var cardStation = MakeCard("Datos de la estación", out var bodyStation, 196);
        int sy = 6;
        bodyStation.Controls.Add(MakeLabel("Código de estación:", LabelX, sy + 2));
        _txtStationCode = MakeTextBox(FieldX, sy, 200);
        _txtStationCode.CharacterCasing = CharacterCasing.Upper;
        bodyStation.Controls.Add(_txtStationCode); sy += 38;
        bodyStation.Controls.Add(MakeLabel("Nombre:", LabelX, sy + 2));
        _txtStationName = MakeTextBox(FieldX, sy, 390);
        bodyStation.Controls.Add(_txtStationName); sy += 38;
        bodyStation.Controls.Add(MakeLabel("Tipo:", LabelX, sy + 2));
        _cmbStationType = new ComboBox
        {
            Location = new Point(FieldX, sy), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList,
            Font = Font, FlatStyle = FlatStyle.Flat
        };
        _cmbStationType.Items.AddRange(Enum.GetNames<StationType>());
        _cmbStationType.SelectedIndex = 0;
        bodyStation.Controls.Add(_cmbStationType); sy += 38;
        bodyStation.Controls.Add(MakeLabel("Línea:", LabelX, sy + 2));
        _txtHostName = MakeTextBox(FieldX, sy, 160);
        bodyStation.Controls.Add(_txtHostName);
        bodyStation.Controls.Add(MakeHint("Línea de producción (M1, M2, ...).", FieldX + 170, sy + 2));
        sy += 38;
        _chkActive = new CheckBox
        {
            Text = "Estación activa", Location = new Point(FieldX, sy), AutoSize = true, Checked = true, Font = Font
        };
        bodyStation.Controls.Add(_chkActive);
        scroll.Controls.Add(cardStation);

        // ── Tarjeta: Seguridad y bloqueo ──
        var cardSec = MakeCard("Seguridad y bloqueo", out var bodySec, 238);
        int qy = 6;
        bodySec.Controls.Add(MakeLabel("Modo de estación:", LabelX, qy + 2));
        _cmbStationMode = new ComboBox
        {
            Location = new Point(FieldX, qy), Width = 160, Font = Font,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbStationMode.Items.AddRange(["Normal", "Free"]);
        _cmbStationMode.SelectedIndex = 0;
        bodySec.Controls.Add(_cmbStationMode);
        bodySec.Controls.Add(MakeHint("Free: sin bloqueo ni login; solo mide tiempo de ajuste.", FieldX + 170, qy + 2));
        qy += 36;
        bodySec.Controls.Add(MakeLabel("Inactividad (min):", LabelX, qy + 2));
        _numAutoLock = MakeNumeric(FieldX, qy, 1, 1440, 5, 1);
        bodySec.Controls.Add(_numAutoLock);
        bodySec.Controls.Add(MakeHint("Minutos sin mouse/teclado antes de bloquear.", FieldX + 100, qy + 2));
        qy += 32;
        _chkNoAutoLock = new CheckBox
        {
            Text = "Sin bloqueo por inactividad (no bloquear nunca)", Location = new Point(FieldX, qy),
            AutoSize = true, Checked = false, Font = Font
        };
        // Al desactivar el bloqueo, el campo de minutos no aplica.
        _chkNoAutoLock.CheckedChanged += (_, _) => _numAutoLock.Enabled = !_chkNoAutoLock.Checked;
        bodySec.Controls.Add(_chkNoAutoLock);
        qy += 36;
        _chkRequireScan = new CheckBox
        {
            Text = "Exigir escáner QR (rechazar tecleo manual)", Location = new Point(FieldX, qy),
            AutoSize = true, Checked = true, Font = Font
        };
        bodySec.Controls.Add(_chkRequireScan); qy += 32;
        bodySec.Controls.Add(MakeLabel("Vel. máx. escáner (ms):", LabelX, qy + 2));
        _numScanMaxMs = MakeNumeric(FieldX, qy, 5, 500, 40, 5);
        bodySec.Controls.Add(_numScanMaxMs);
        bodySec.Controls.Add(MakeHint("ms promedio máx. entre teclas para ser escáner.", FieldX + 100, qy + 2));
        qy += 38;
        bodySec.Controls.Add(MakeLabel("PIN local (offline):", LabelX, qy + 2));
        _txtAdminPin = MakeTextBox(FieldX, qy, 150);
        _txtAdminPin.UseSystemPasswordChar = true;
        bodySec.Controls.Add(_txtAdminPin);
        bodySec.Controls.Add(MakeHint("Respaldo de admin cuando no hay conexión.", FieldX + 160, qy + 2));
        scroll.Controls.Add(cardSec);

        // ── Tarjeta: Input QR externo ──
        var cardQrInput = MakeCard("Input QR externo", out var bodyQrInput, 256);
        _chkQrInputFocusEnabled = new CheckBox
        {
            Text = "Enfocar input externo al iniciar sesión",
            Location = new Point(LabelX, 6),
            AutoSize = true,
            Checked = false,
            Font = Font
        };
        bodyQrInput.Controls.Add(_chkQrInputFocusEnabled);
        bodyQrInput.Controls.Add(MakeLabel("Espera (ms):", 365, 6));
        _numQrInputFocusDelay = MakeNumeric(466, 4, 0, 10000, 500, 100);
        bodyQrInput.Controls.Add(_numQrInputFocusDelay);

        bodyQrInput.Controls.Add(MakeLabel("Proceso:", LabelX, 42));
        _txtQrInputProcessName = MakeTextBox(FieldX, 40, 150);
        bodyQrInput.Controls.Add(_txtQrInputProcessName);
        bodyQrInput.Controls.Add(MakeHint("Sin .exe", FieldX + 160, 42));

        bodyQrInput.Controls.Add(MakeLabel("Ventana:", LabelX, 78));
        _txtQrInputWindowTitle = MakeTextBox(FieldX, 76, 350);
        bodyQrInput.Controls.Add(_txtQrInputWindowTitle);

        _cmbQrInputWindows = new ComboBox
        {
            Location = new Point(LabelX, 112),
            Width = 330,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = Font,
            FlatStyle = FlatStyle.Flat
        };
        bodyQrInput.Controls.Add(_cmbQrInputWindows);
        _btnRefreshQrInputWindows = MakeButton("Detectar", 356, 108, 86, Color.FromArgb(50, 110, 200));
        _btnRefreshQrInputWindows.Height = 32;
        _btnRefreshQrInputWindows.Click += (_, _) => RefreshQrInputWindowsList();
        bodyQrInput.Controls.Add(_btnRefreshQrInputWindows);
        _btnUseQrInputWindow = MakeButton("Usar", 452, 108, 86, Branding.Green);
        _btnUseQrInputWindow.Height = 32;
        _btnUseQrInputWindow.Click += (_, _) => UseSelectedQrInputWindow();
        bodyQrInput.Controls.Add(_btnUseQrInputWindow);

        _cmbQrInputTargets = new ComboBox
        {
            Location = new Point(LabelX, 152),
            Width = 330,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = Font,
            FlatStyle = FlatStyle.Flat
        };
        _cmbQrInputTargets.SelectedIndexChanged += (_, _) =>
        {
            if (_cmbQrInputTargets.SelectedItem is QrInputCandidate candidate)
                _selectedQrInputTarget = candidate;
        };
        bodyQrInput.Controls.Add(_cmbQrInputTargets);
        _btnRefreshQrInputTargets = MakeButton("Inputs", 356, 148, 86, Color.FromArgb(50, 110, 200));
        _btnRefreshQrInputTargets.Height = 32;
        _btnRefreshQrInputTargets.Click += (_, _) => RefreshQrInputTargetsList();
        bodyQrInput.Controls.Add(_btnRefreshQrInputTargets);
        _btnTestQrInputFocus = MakeButton("Probar", 452, 148, 86, Branding.Green);
        _btnTestQrInputFocus.Height = 32;
        _btnTestQrInputFocus.Click += async (_, _) => await TestQrInputFocusAsync();
        bodyQrInput.Controls.Add(_btnTestQrInputFocus);

        _chkQrInputFallbackClick = new CheckBox
        {
            Text = "Usar punto si no detecta input",
            Location = new Point(LabelX, 190),
            AutoSize = true,
            Checked = false,
            Font = Font
        };
        bodyQrInput.Controls.Add(_chkQrInputFallbackClick);
        _txtQrInputFallbackPoint = MakeTextBox(246, 188, 104);
        _txtQrInputFallbackPoint.ReadOnly = true;
        bodyQrInput.Controls.Add(_txtQrInputFallbackPoint);
        _btnCaptureQrInputPoint = MakeButton("Punto", 356, 184, 86, Color.FromArgb(50, 110, 200));
        _btnCaptureQrInputPoint.Height = 32;
        _btnCaptureQrInputPoint.Click += async (_, _) => await CaptureQrInputFallbackPointAsync();
        bodyQrInput.Controls.Add(_btnCaptureQrInputPoint);

        bodyQrInput.Controls.Add(MakeHint("Orden: input detectado; si falla, click en el punto guardado.", LabelX, 224));
        scroll.Controls.Add(cardQrInput);

        // ── Tarjeta: Ventanas protegidas ──
        var cardWindows = MakeCard("Ventanas protegidas", out var bodyWindows, 312);
        _chkWindowGuardEnabled = new CheckBox
        {
            Text = "Activar bloqueo de ventanas externas",
            Location = new Point(LabelX, 6),
            AutoSize = true,
            Checked = true,
            Font = Font
        };
        bodyWindows.Controls.Add(_chkWindowGuardEnabled);
        // Si se desmarca, el overlay de autorización solo acepta escáner (sin usuario+contraseña).
        _chkWindowGuardManualLogin = new CheckBox
        {
            Text = "Pedir usuario y contraseña al autorizar (si se desmarca: solo escáner)",
            Location = new Point(LabelX, 284),
            AutoSize = true,
            Checked = true,
            Font = Font
        };
        bodyWindows.Controls.Add(_chkWindowGuardManualLogin);
        bodyWindows.Controls.Add(MakeLabel("Revisión (ms):", 365, 6));
        _numWindowGuardPollMs = MakeNumeric(466, 4, 250, 10000, 500, 250);
        bodyWindows.Controls.Add(_numWindowGuardPollMs);

        _cmbOpenWindows = new ComboBox
        {
            Location = new Point(LabelX, 42),
            Width = 330,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = Font,
            FlatStyle = FlatStyle.Flat
        };
        bodyWindows.Controls.Add(_cmbOpenWindows);
        _btnRefreshWindows = MakeButton("Detectar", 356, 38, 86, Color.FromArgb(50, 110, 200));
        _btnRefreshWindows.Height = 32;
        _btnRefreshWindows.Click += (_, _) => RefreshOpenWindowsList();
        bodyWindows.Controls.Add(_btnRefreshWindows);
        _btnUseSelectedWindow = MakeButton("Usar", 452, 38, 86, Branding.Green);
        _btnUseSelectedWindow.Height = 32;
        _btnUseSelectedWindow.Click += (_, _) => UseSelectedOpenWindow();
        bodyWindows.Controls.Add(_btnUseSelectedWindow);

        _gridWindowRules = new DataGridView
        {
            Location = new Point(LabelX, 82),
            Size = new Size(CardWidth - 58, 154),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            EditMode = DataGridViewEditMode.EditOnEnter,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        _gridWindowRules.DataError += (_, _) => { };
        _gridWindowRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Regla", Width = 120 });
        _gridWindowRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProcessName", HeaderText = "Proceso", Width = 95 });
        _gridWindowRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "WindowTitle", HeaderText = "Ventana", Width = 150 });
        _gridWindowRules.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "MatchMode",
            HeaderText = "Coinc.",
            Width = 88,
            DataSource = Enum.GetNames<WindowTitleMatchMode>()
        });
        _gridWindowRules.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "BlockAction",
            HeaderText = "Acción",
            Width = 82,
            DataSource = Enum.GetNames<WindowBlockAction>()
        });
        _gridWindowRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "AllowedRoles", HeaderText = "Roles permitidos", Width = 170 });
        _gridWindowRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "AllowedUsers", HeaderText = "Usuarios permitidos", Width = 150 });
        bodyWindows.Controls.Add(_gridWindowRules);

        _btnAddWindowRule = MakeButton("+ Regla", LabelX, 246, 112, Branding.Green);
        _btnAddWindowRule.Height = 32;
        _btnAddWindowRule.Click += (_, _) => AddWindowRuleRow(new WindowRuleEditorModel());
        bodyWindows.Controls.Add(_btnAddWindowRule);
        _btnRemoveWindowRule = MakeButton("Quitar", LabelX + 122, 246, 112, Color.FromArgb(180, 40, 40));
        _btnRemoveWindowRule.Height = 32;
        _btnRemoveWindowRule.Click += (_, _) => RemoveSelectedWindowRule();
        bodyWindows.Controls.Add(_btnRemoveWindowRule);
        bodyWindows.Controls.Add(MakeHint("Roles/usuarios separados por coma. Si se dejan vacíos, se permite a todos.", LabelX + 246, 252));
        scroll.Controls.Add(cardWindows);

        // ── Tarjeta: Acciones ──
        var cardActions = MakeCard("Acciones", out var bodyActions, 150);
        _btnRegister = MakeButton("Registrar / Actualizar estación en BD", LabelX, 6, CardWidth - 56, Branding.Green);
        _btnRegister.Click += BtnRegister_Click;
        bodyActions.Controls.Add(_btnRegister);
        _btnSaveConfig = MakeButton("Guardar configuración local (appsettings.json)", LabelX, 50, CardWidth - 56, Color.FromArgb(50, 110, 200));
        _btnSaveConfig.Click += BtnSaveConfig_Click;
        bodyActions.Controls.Add(_btnSaveConfig);
        _btnStartService = MakeButton("▶  Iniciar bloqueo", LabelX, 94, (CardWidth - 66) / 2, Branding.Green);
        _btnStartService.Click += BtnStartService_Click;
        bodyActions.Controls.Add(_btnStartService);
        _btnStopService = MakeButton("⏹  Detener servicio", LabelX + (CardWidth - 66) / 2 + 10, 94, (CardWidth - 66) / 2, Color.FromArgb(180, 40, 40));
        _btnStopService.Click += BtnStopService_Click;
        bodyActions.Controls.Add(_btnStopService);
        scroll.Controls.Add(cardActions);

        // ── Tarjeta: Estado ──
        var cardStatus = MakeCard("Estado", out var bodyStatus, 80);
        _lblCacheCount = new Label
        {
            AutoSize = false, Location = new Point(LabelX, 6), Size = new Size(CardWidth - 40, 22),
            ForeColor = Color.DimGray, Text = "Cargando…"
        };
        _lblResult = new Label
        {
            AutoSize = false, Location = new Point(LabelX, 32), Size = new Size(CardWidth - 40, 40),
            Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.DimGray
        };
        bodyStatus.Controls.Add(_lblCacheCount);
        bodyStatus.Controls.Add(_lblResult);
        scroll.Controls.Add(cardStatus);

        ResumeLayout(true);
        RefreshStatus();
    }

    /// <summary>Crea una tarjeta con título y devuelve su panel de cuerpo para añadir controles.</summary>
    private CardPanel MakeCard(string title, out Panel body, int bodyHeight)
    {
        var card = new CardPanel
        {
            Width = CardWidth,
            Height = bodyHeight + 46,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(14, 12, 14, 12)
        };
        var lblTitle = new Label
        {
            Text = title, AutoSize = true, Location = new Point(14, 10),
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), ForeColor = Branding.GreenDark,
            BackColor = Color.Transparent
        };
        body = new Panel
        {
            Location = new Point(10, 40), Size = new Size(CardWidth - 24, bodyHeight),
            BackColor = Color.White   // mismo color del card: sin contraste alrededor de los controles
        };
        card.Controls.Add(lblTitle);
        card.Controls.Add(body);
        return card;
    }

    private NumericUpDown MakeNumeric(int x, int y, int min, int max, int val, int step) =>
        new()
        {
            Location = new Point(x, y), Width = 84, Font = Font,
            Minimum = min, Maximum = max, Value = val, Increment = step,
            BorderStyle = BorderStyle.FixedSingle
        };

    // ─────────────────────────────────────────────────────────
    // Load current config from appsettings.json
    // ─────────────────────────────────────────────────────────

    private void LoadCurrentConfig()
    {
        try
        {
            var config = LoadConfig();

            _txtApiUrl.Text = config["ApiBaseUrl"] ?? _api.BaseAddress;
            _txtStationCode.Text = config["StationCode"] ?? string.Empty;
            // Tipo de estacion (ICT/FCT/Packing/Vision): selecciona el guardado si es valido.
            var savedType = config["StationType"];
            if (!string.IsNullOrWhiteSpace(savedType) && _cmbStationType.Items.Contains(savedType))
                _cmbStationType.SelectedItem = savedType;
            // Linea (host_name): de config si ya se guardo, si no el nombre del equipo.
            _txtHostName.Text = config["Linea"] ?? Environment.MachineName;

            // Pre-fill station name if code is set
            if (!string.IsNullOrEmpty(_txtStationCode.Text))
                _txtStationName.Text = _txtStationCode.Text;

            // Seguridad / bloqueo. AutoLockSeconds se guarda en SEGUNDOS, pero el campo
            // se muestra en MINUTOS (redondeando hacia arriba al minuto mas cercano).
            // AutoLockSeconds == 0 => bloqueo por inactividad desactivado.
            var hasAutoLock = int.TryParse(config["AutoLockSeconds"], out var sec);
            _chkNoAutoLock.Checked = hasAutoLock && sec == 0;
            _numAutoLock.Enabled = !_chkNoAutoLock.Checked;
            if (hasAutoLock && sec > 0)
            {
                var minutes = Math.Max(1, (int)Math.Round(sec / 60.0));
                if (minutes <= _numAutoLock.Maximum) _numAutoLock.Value = minutes;
            }
            _cmbStationMode.SelectedItem =
                string.Equals(config["StationMode"], "Free", StringComparison.OrdinalIgnoreCase) ? "Free" : "Normal";
            _chkRequireScan.Checked = !string.Equals(config["RequireScan"], "false", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(config["ScanMaxAvgKeyMs"], out var ms) && ms >= _numScanMaxMs.Minimum && ms <= _numScanMaxMs.Maximum)
                _numScanMaxMs.Value = ms;
            _txtAdminPin.Text = config["AdminPin"] ?? string.Empty;
            LoadQrInputFocusUi(LoadJsonSection("QrInputFocus") ?? DefaultQrInputFocusJson());
            RefreshQrInputWindowsList();
            LoadWindowGuardUi(LoadJsonSection("WindowAccessGuard") ?? DefaultWindowAccessGuardJson());
            RefreshOpenWindowsList();
        }
        catch
        {
            _txtApiUrl.Text = _api.BaseAddress;
            _txtHostName.Text = Environment.MachineName;
            LoadQrInputFocusUi(DefaultQrInputFocusJson());
            RefreshQrInputWindowsList();
            LoadWindowGuardUi(DefaultWindowAccessGuardJson());
            RefreshOpenWindowsList();
        }
    }

    private void RefreshStatus()
    {
        var cache = _localState.LoadOperatorCache();
        _lblCacheCount.Text = $"Caché local: {cache.Count} operadores autorizados  |  " +
                              $"Hostname: {Environment.MachineName}  |  " +
                              $"OS: {Environment.OSVersion.VersionString}";
    }

    // ─────────────────────────────────────────────────────────
    // Button handlers
    // ─────────────────────────────────────────────────────────

    private async void BtnTestConnection_Click(object? sender, EventArgs e)
    {
        _btnTestConnection.Enabled = false;
        _lblApiStatus.Text = "Probando…";
        _lblApiStatus.ForeColor = Color.Gray;

        var ok = await _api.IsAvailableAsync();

        _lblApiStatus.Text = ok
            ? $"✔  Conectado correctamente a {_api.BaseAddress}"
            : $"✖  No se pudo conectar a {_api.BaseAddress}";
        _lblApiStatus.ForeColor = ok ? Color.Green : Color.Red;
        _btnTestConnection.Enabled = true;
    }

    private async void BtnRegister_Click(object? sender, EventArgs e)
    {
        if (!ValidateStationFields()) return;

        _btnRegister.Enabled = false;
        SetResult("Registrando…", Color.Gray);

        // Token claims are scoped to the station code being registered.
        _api.SetStationCode(_txtStationCode.Text.Trim());

        // El campo "Línea" (host_name en BD) identifica la linea de produccion (M1, M2...).
        // Si esta vacio, se usa el nombre del equipo como respaldo.
        var linea = string.IsNullOrWhiteSpace(_txtHostName.Text) ? Environment.MachineName : _txtHostName.Text.Trim();
        _api.Line = linea;   // para que el bootstrap posterior consulte la estacion correcta

        var request = new RegisterStationRequest(
            _txtStationCode.Text.Trim(),
            _txtStationName.Text.Trim(),
            Enum.Parse<StationType>(_cmbStationType.SelectedItem!.ToString()!),
            linea,
            _chkActive.Checked);

        var (ok, message) = await _api.RegisterStationAsync(request);

        SetResult(ok ? $"✔  {message}" : $"✖  {message}",
                  ok ? Color.Green : Color.Red);

        if (ok)
        {
            // Refresh local cache from newly registered station
            var bootstrap = await _api.GetBootstrapAsync(_txtStationCode.Text.Trim());
            if (bootstrap is not null)
            {
                _localState.SaveOperatorCache(bootstrap.AllowedOperators);
                RefreshStatus();
            }
        }

        _btnRegister.Enabled = true;
    }

    private void BtnSaveConfig_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtStationCode.Text))
        {
            SetResult("✖  Ingrese el código de estación antes de guardar.", Color.Red);
            return;
        }

        try
        {
            // Preserve existing secrets/keys instead of overwriting them with placeholders.
            var existing = LoadConfig();

            // Si el campo del PIN quedo vacio, conserva el PIN existente: nunca lo borres
            // al guardar (dejaria la estacion sin via de recuperacion offline).
            var pin = string.IsNullOrWhiteSpace(_txtAdminPin.Text)
                ? (existing["AdminPin"] ?? string.Empty)
                : _txtAdminPin.Text.Trim();

            var root = new JsonObject
            {
                ["StationCode"] = _txtStationCode.Text.Trim(),
                ["StationType"] = _cmbStationType.SelectedItem?.ToString() ?? "ICT",
                ["Linea"] = _txtHostName.Text.Trim(),
                ["ApiBaseUrl"] = _txtApiUrl.Text.Trim().TrimEnd('/') + "/",
                ["BypassHmacSecret"] = existing["BypassHmacSecret"] ?? string.Empty,
                ["AdminPin"] = pin,
                ["ClientApiKey"] = existing["ClientApiKey"] ?? string.Empty,
                ["StationMode"] = _cmbStationMode.SelectedItem?.ToString() ?? "Normal",
                ["AutoLockSeconds"] = _chkNoAutoLock.Checked ? 0 : (int)_numAutoLock.Value * 60,   // 0 = desactivado; minutos (UI) -> segundos
                ["RequireScan"] = _chkRequireScan.Checked,
                ["ScanMaxAvgKeyMs"] = (int)_numScanMaxMs.Value
            };

            _gridWindowRules.EndEdit();
            var windowGuard = BuildWindowGuardJsonFromUi();
            if (windowGuard is not null)
                root["WindowAccessGuard"] = windowGuard;

            root["QrInputFocus"] = BuildQrInputFocusJsonFromUi();

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath) ?? AppConstants.LocalDataPath);
            File.WriteAllText(_configPath, json);
            SetResult("✔  Configuración guardada.", Color.Green);

            MessageBox.Show(
                "La configuración se guardó correctamente.\n\n" +
                "El foco QR externo y las ventanas protegidas se aplican al cerrar " +
                "esta configuración. Cambios como URL, inactividad y escáner se aplican " +
                "al reiniciar la pantalla de bloqueo.",
                "Configuración guardada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetResult($"✖  Error al guardar: {ex.Message}", Color.Red);
        }
    }

    private void BtnStartService_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtStationCode.Text))
        {
            SetResult("✖  Configure el código de estación primero.", Color.Red);
            return;
        }

        SetResult("Iniciando pantalla de bloqueo…", Color.Gray);
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void BtnStopService_Click(object? sender, EventArgs e)
    {
        var confirm = MessageBox.Show(
            "¿Está seguro de que desea detener el servicio QualityLock?\n\n" +
            "La estación quedará sin bloqueo hasta que se reinicie el servicio.",
            "Detener servicio",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        var auth = await AdminLoginPrompt.AuthenticateAsync(_adminPin, this);
        if (auth is { Authenticated: true })
        {
            SetResult($"Servicio detenido por {auth.Value.DisplayName}.", Color.DarkRed);
            // Señala "detener servicio". Si este dialogo lo abrio el LockForm (desde la
            // bandeja), este cierra la sesion activa antes de salir; si se abrio en el
            // arranque (--setup), no hay sesion que cerrar y se sale igual.
            StopServiceRequested = true;
            DialogResult = DialogResult.Abort;
            Close();
        }
        else
        {
            SetResult("✖  Acceso denegado. Servicio no detenido.", Color.Red);
        }
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private bool ValidateStationFields()
    {
        if (string.IsNullOrWhiteSpace(_txtStationCode.Text))
        {
            SetResult("✖  El código de estación es obligatorio.", Color.Red);
            return false;
        }
        if (string.IsNullOrWhiteSpace(_txtStationName.Text))
        {
            SetResult("✖  El nombre de la estación es obligatorio.", Color.Red);
            return false;
        }
        return true;
    }

    private void SetResult(string text, Color color)
    {
        _lblResult.Text = text;
        _lblResult.ForeColor = color;
    }

    private IConfigurationRoot LoadConfig()
    {
        var exeConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var builder = new ConfigurationBuilder();

        if (File.Exists(exeConfigPath))
            builder.AddJsonFile(exeConfigPath, optional: true);

        if (File.Exists(_configPath))
            builder.AddJsonFile(_configPath, optional: true);

        return builder.Build();
    }

    private JsonNode? LoadJsonSection(string sectionName)
    {
        var exeConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        foreach (var path in new[] { _configPath, exeConfigPath })
        {
            try
            {
                if (!File.Exists(path)) continue;

                var doc = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                if (doc?[sectionName] is JsonNode section)
                    return section.DeepClone();
            }
            catch
            {
                // Si el JSON esta corrupto, el guard tiene default en codigo.
            }
        }

        return null;
    }

    private void LoadQrInputFocusUi(JsonNode? qrInputFocus)
    {
        var obj = qrInputFocus as JsonObject;
        _chkQrInputFocusEnabled.Checked = JsonBool(obj, "Enabled", false);

        var delay = JsonInt(obj, "DelayMilliseconds", 500);
        delay = Math.Clamp(delay, (int)_numQrInputFocusDelay.Minimum, (int)_numQrInputFocusDelay.Maximum);
        _numQrInputFocusDelay.Value = delay;

        _txtQrInputProcessName.Text = JsonString(obj, "ProcessName");
        _txtQrInputWindowTitle.Text = JsonString(obj, "WindowTitle");

        var inputObject = obj?["Input"] as JsonObject;
        _selectedQrInputTarget = ReadQrInputCandidate(inputObject);
        _cmbQrInputTargets.Items.Clear();
        if (_selectedQrInputTarget is not null)
        {
            _cmbQrInputTargets.Items.Add(_selectedQrInputTarget);
            _cmbQrInputTargets.SelectedIndex = 0;
        }

        _chkQrInputFallbackClick.Checked = JsonBool(inputObject, "UseFallbackClick", false);
        var fallbackX = JsonInt(inputObject, "FallbackClickX", -1);
        var fallbackY = JsonInt(inputObject, "FallbackClickY", -1);
        _qrInputFallbackPoint = fallbackX >= 0 && fallbackY >= 0 ? new Point(fallbackX, fallbackY) : null;
        UpdateQrInputFallbackPointText();
    }

    private JsonNode BuildQrInputFocusJsonFromUi()
    {
        var input = BuildQrInputTargetFromUi();

        return new JsonObject
        {
            ["Enabled"] = _chkQrInputFocusEnabled.Checked,
            ["DelayMilliseconds"] = (int)_numQrInputFocusDelay.Value,
            ["RetryCount"] = 3,
            ["ProcessName"] = _txtQrInputProcessName.Text.Trim(),
            ["WindowTitle"] = _txtQrInputWindowTitle.Text.Trim(),
            ["MatchMode"] = WindowTitleMatchMode.Exact.ToString(),
            ["Input"] = QrInputTargetToJson(input)
        };
    }

    private QrInputFocusOptions BuildQrInputFocusOptionsFromUi()
    {
        var input = BuildQrInputTargetFromUi();
        return new QrInputFocusOptions
        {
            Enabled = _chkQrInputFocusEnabled.Checked,
            DelayMilliseconds = (int)_numQrInputFocusDelay.Value,
            RetryCount = 1,
            ProcessName = _txtQrInputProcessName.Text.Trim(),
            WindowTitle = _txtQrInputWindowTitle.Text.Trim(),
            MatchMode = WindowTitleMatchMode.Exact,
            Input = input
        };
    }

    private QrInputTarget BuildQrInputTargetFromUi()
    {
        var selected = _selectedQrInputTarget?.ToTarget() ?? new QrInputTarget();
        return new QrInputTarget
        {
            AutomationId = selected.AutomationId,
            Name = selected.Name,
            ClassName = selected.ClassName,
            ControlType = selected.ControlType,
            ControlIndex = selected.ControlIndex,
            NativeWindowHandle = selected.NativeWindowHandle,
            UseFallbackClick = _chkQrInputFallbackClick.Checked && _qrInputFallbackPoint.HasValue,
            FallbackClickX = _qrInputFallbackPoint?.X ?? -1,
            FallbackClickY = _qrInputFallbackPoint?.Y ?? -1
        };
    }

    private void RefreshQrInputWindowsList()
    {
        _cmbQrInputWindows.Items.Clear();

        foreach (var window in ExternalInputFocusService.GetOpenWindows())
            _cmbQrInputWindows.Items.Add(window);

        if (_cmbQrInputWindows.Items.Count > 0)
            _cmbQrInputWindows.SelectedIndex = 0;
    }

    private void UseSelectedQrInputWindow()
    {
        if (_cmbQrInputWindows.SelectedItem is not InputFocusWindowSnapshot window)
        {
            SetResult("No hay ventana externa detectada para el input QR.", Color.OrangeRed);
            return;
        }

        _txtQrInputProcessName.Text = window.ProcessName;
        _txtQrInputWindowTitle.Text = window.WindowTitle;
        _chkQrInputFocusEnabled.Checked = true;
        _selectedQrInputTarget = null;
        _qrInputFallbackPoint = null;
        _chkQrInputFallbackClick.Checked = false;
        UpdateQrInputFallbackPointText();
        _cmbQrInputTargets.Items.Clear();
        SetResult($"Destino QR: {window.ProcessName}.exe / {window.WindowTitle}", Color.Green);
    }

    private void RefreshQrInputTargetsList()
    {
        var processName = _txtQrInputProcessName.Text.Trim();
        var windowTitle = _txtQrInputWindowTitle.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(windowTitle))
        {
            SetResult("Seleccione primero la ventana del programa externo.", Color.OrangeRed);
            return;
        }

        _cmbQrInputTargets.Items.Clear();
        foreach (var candidate in ExternalInputFocusService.GetInputCandidates(
            processName,
            windowTitle,
            WindowTitleMatchMode.Exact))
        {
            _cmbQrInputTargets.Items.Add(candidate);
        }

        if (_cmbQrInputTargets.Items.Count == 0)
        {
            _selectedQrInputTarget = null;
            SetResult("No se detectaron inputs text. Use Punto para guardar coordenada de respaldo.", Color.OrangeRed);
            return;
        }

        _cmbQrInputTargets.SelectedIndex = 0;
        _chkQrInputFocusEnabled.Checked = true;
        SetResult($"Inputs detectados: {_cmbQrInputTargets.Items.Count}", Color.Green);
    }

    private async Task TestQrInputFocusAsync()
    {
        _btnTestQrInputFocus.Enabled = false;
        try
        {
            await WaitForCurrentMouseClickToFinishAsync();

            var options = BuildQrInputFocusOptionsFromUi();
            if (string.IsNullOrWhiteSpace(options.ProcessName) && string.IsNullOrWhiteSpace(options.WindowTitle))
            {
                SetResult("Configure ventana e input QR antes de probar.", Color.OrangeRed);
                return;
            }

            var focusService = new ExternalInputFocusService(options);
            AppendQrInputFocusLog($"Setup Probar starting QR focus. {focusService.DiagnosticDescription}");
            var ok = focusService.TryFocusOnce(AppendQrInputFocusLog);
            AppendQrInputFocusLog($"Setup Probar QR focus completed: {ok}.");
            if (ok && !_chkQrInputFocusEnabled.Checked)
            {
                _chkQrInputFocusEnabled.Checked = true;
                SetResult("Foco aplicado. Se activo tambien para el proximo desbloqueo; guarde la configuracion.", Color.Green);
                return;
            }

            SetResult(ok ? "Foco aplicado al input QR. Guarde la configuracion para usarlo al desbloquear." : "No se pudo enfocar el input QR.",
                ok ? Color.Green : Color.OrangeRed);
        }
        finally
        {
            _btnTestQrInputFocus.Enabled = true;
        }
    }

    private static async Task WaitForCurrentMouseClickToFinishAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(750);
        while (ExternalInputFocusService.IsLeftMouseButtonDown() && DateTime.UtcNow < deadline)
            await Task.Delay(15);

        await Task.Delay(120);
    }

    private static void AppendQrInputFocusLog(string message)
    {
        try
        {
            Directory.CreateDirectory(AppConstants.LogsFolder);
            var path = Path.Combine(AppConstants.LogsFolder, "qr-input-focus.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostico best-effort.
        }
    }

    private async Task CaptureQrInputFallbackPointAsync()
    {
        _btnCaptureQrInputPoint.Enabled = false;
        SetResult("Haga click en el input externo. La ventana se tomara desde ese click.", Color.Gray);

        var previousState = WindowState;
        InputFocusPointCapture? capture = null;
        var clickDetected = false;

        try
        {
            WindowState = FormWindowState.Minimized;
            await Task.Delay(250);

            var deadline = DateTime.UtcNow.AddSeconds(10);

            // Ignora el click que presiono el boton Punto.
            while (ExternalInputFocusService.IsLeftMouseButtonDown() && DateTime.UtcNow < deadline)
                await Task.Delay(25);

            while (DateTime.UtcNow < deadline)
            {
                if (ExternalInputFocusService.IsLeftMouseButtonDown())
                {
                    clickDetected = true;
                    if (ExternalInputFocusService.TryCaptureFallbackClickPointFromCursor(out var pointCapture))
                    {
                        capture = pointCapture;
                    }

                    while (ExternalInputFocusService.IsLeftMouseButtonDown() && DateTime.UtcNow < deadline)
                        await Task.Delay(25);

                    break;
                }

                await Task.Delay(25);
            }

            // Respaldo: si no hubo click, intenta guardar donde quedo el mouse.
            if (capture is null &&
                ExternalInputFocusService.TryCaptureFallbackClickPointFromCursor(out var pointAtCursor))
            {
                capture = pointAtCursor;
            }
        }
        finally
        {
            WindowState = previousState == FormWindowState.Minimized ? FormWindowState.Normal : previousState;
            Activate();
            _btnCaptureQrInputPoint.Enabled = true;
        }

        if (capture is null)
        {
            SetResult(clickDetected
                    ? "Click detectado, pero Windows no entrego una ventana valida debajo del mouse."
                    : "No se detecto click sobre una ventana valida.",
                Color.OrangeRed);
            return;
        }

        _txtQrInputProcessName.Text = capture.Window.ProcessName;
        _txtQrInputWindowTitle.Text = capture.Window.WindowTitle;
        _selectedQrInputTarget = null;
        _cmbQrInputTargets.Items.Clear();
        _qrInputFallbackPoint = capture.ClientPoint;
        _chkQrInputFocusEnabled.Checked = true;
        _chkQrInputFallbackClick.Checked = true;
        UpdateQrInputFallbackPointText();
        SetResult(
            $"Punto guardado en {capture.Window.ProcessName}.exe: {capture.ClientPoint.X}, {capture.ClientPoint.Y}",
            Color.Green);
    }

    private void UpdateQrInputFallbackPointText()
    {
        _txtQrInputFallbackPoint.Text = _qrInputFallbackPoint is { } point
            ? $"{point.X}, {point.Y}"
            : "";
    }

    private static QrInputCandidate? ReadQrInputCandidate(JsonObject? input)
    {
        if (input is null)
            return null;

        var automationId = JsonString(input, "AutomationId");
        var name = JsonString(input, "Name");
        var className = JsonString(input, "ClassName");
        var controlType = JsonString(input, "ControlType");
        var controlIndex = JsonInt(input, "ControlIndex", 0);
        var nativeWindowHandle = JsonInt(input, "NativeWindowHandle", 0);

        if (string.IsNullOrWhiteSpace(automationId) &&
            string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(className) &&
            string.IsNullOrWhiteSpace(controlType) &&
            nativeWindowHandle == 0)
        {
            return null;
        }

        return new QrInputCandidate(
            "Config",
            automationId,
            name,
            className,
            controlType,
            controlIndex,
            nativeWindowHandle);
    }

    private static JsonObject QrInputTargetToJson(QrInputTarget input)
        => new()
        {
            ["AutomationId"] = input.AutomationId,
            ["Name"] = input.Name,
            ["ClassName"] = input.ClassName,
            ["ControlType"] = input.ControlType,
            ["ControlIndex"] = input.ControlIndex,
            ["NativeWindowHandle"] = input.NativeWindowHandle,
            ["UseFallbackClick"] = input.UseFallbackClick,
            ["FallbackClickX"] = input.FallbackClickX,
            ["FallbackClickY"] = input.FallbackClickY
        };

    private void LoadWindowGuardUi(JsonNode? windowGuard)
    {
        _gridWindowRules.Rows.Clear();

        var obj = windowGuard as JsonObject;
        _chkWindowGuardEnabled.Checked = JsonBool(obj, "Enabled", true);
        _chkWindowGuardManualLogin.Checked = JsonBool(obj, "AllowManualLogin", true);

        var poll = JsonInt(obj, "PollMilliseconds", 500);
        poll = Math.Clamp(poll, (int)_numWindowGuardPollMs.Minimum, (int)_numWindowGuardPollMs.Maximum);
        _numWindowGuardPollMs.Value = poll;

        var rules = obj?["Rules"] as JsonArray;
        if (rules is not null)
        {
            foreach (var node in rules)
            {
                if (node is not JsonObject rule) continue;

                AddWindowRuleRow(new WindowRuleEditorModel
                {
                    Name = JsonString(rule, "Name"),
                    ProcessName = JsonString(rule, "ProcessName"),
                    WindowTitle = JsonString(rule, "WindowTitle"),
                    MatchMode = NormalizeEnumName(JsonString(rule, "MatchMode"), WindowTitleMatchMode.Exact),
                    BlockAction = NormalizeEnumName(JsonString(rule, "BlockAction"), WindowBlockAction.Close),
                    AllowedRoles = JsonStringList(rule, "AllowedRoles"),
                    AllowedUsers = JsonStringList(rule, "AllowedUsers")
                });
            }
        }

        if (_gridWindowRules.Rows.Count == 0 && rules is null)
            AddWindowRuleRow(new WindowRuleEditorModel
            {
                Name = "AT-01 Error View",
                ProcessName = "inctest",
                WindowTitle = "Error View"
            });
    }

    private JsonNode BuildWindowGuardJsonFromUi()
    {
        var rules = new JsonArray();

        foreach (DataGridViewRow row in _gridWindowRules.Rows)
        {
            if (row.IsNewRow) continue;

            var processName = GetCellString(row, "ProcessName");
            var windowTitle = GetCellString(row, "WindowTitle");
            if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(windowTitle))
                continue;

            var name = GetCellString(row, "Name");
            if (string.IsNullOrWhiteSpace(name))
                name = $"{processName} {windowTitle}".Trim();

            rules.Add(new JsonObject
            {
                ["Name"] = name,
                ["ProcessName"] = processName,
                ["WindowTitle"] = windowTitle,
                ["MatchMode"] = NormalizeEnumName(GetCellString(row, "MatchMode"), WindowTitleMatchMode.Exact),
                ["BlockAction"] = NormalizeEnumName(GetCellString(row, "BlockAction"), WindowBlockAction.Close),
                ["AllowedRoles"] = SplitListToJsonArray(GetCellString(row, "AllowedRoles")),
                ["AllowedUsers"] = SplitListToJsonArray(GetCellString(row, "AllowedUsers"))
            });
        }

        return new JsonObject
        {
            ["Enabled"] = _chkWindowGuardEnabled.Checked,
            ["AllowManualLogin"] = _chkWindowGuardManualLogin.Checked,
            ["PollMilliseconds"] = (int)_numWindowGuardPollMs.Value,
            ["Rules"] = rules
        };
    }

    private DataGridViewRow AddWindowRuleRow(WindowRuleEditorModel model)
    {
        var rowIndex = _gridWindowRules.Rows.Add(
            string.IsNullOrWhiteSpace(model.Name) ? "Nueva regla" : model.Name.Trim(),
            model.ProcessName.Trim(),
            model.WindowTitle.Trim(),
            NormalizeEnumName(model.MatchMode, WindowTitleMatchMode.Exact),
            NormalizeEnumName(model.BlockAction, WindowBlockAction.Close),
            string.IsNullOrWhiteSpace(model.AllowedRoles) ? "superadmin, Tecnico QA" : model.AllowedRoles.Trim(),
            model.AllowedUsers.Trim());

        var row = _gridWindowRules.Rows[rowIndex];
        row.Selected = true;
        _gridWindowRules.CurrentCell = row.Cells["Name"];
        return row;
    }

    private void RemoveSelectedWindowRule()
    {
        if (_gridWindowRules.SelectedRows.Count == 0)
        {
            SetResult("Seleccione una regla para quitar.", Color.OrangeRed);
            return;
        }

        _gridWindowRules.Rows.Remove(_gridWindowRules.SelectedRows[0]);
    }

    private void RefreshOpenWindowsList()
    {
        _cmbOpenWindows.Items.Clear();

        foreach (var window in ExternalWindowGuardService.GetOpenWindows())
        {
            _cmbOpenWindows.Items.Add(new WindowCandidate(window.ProcessName, window.WindowTitle));
        }

        if (_cmbOpenWindows.Items.Count > 0)
            _cmbOpenWindows.SelectedIndex = 0;
    }

    private void UseSelectedOpenWindow()
    {
        if (_cmbOpenWindows.SelectedItem is not WindowCandidate window)
        {
            SetResult("No hay ventana detectada para usar.", Color.OrangeRed);
            return;
        }

        var row = _gridWindowRules.SelectedRows.Count > 0
            ? _gridWindowRules.SelectedRows[0]
            : AddWindowRuleRow(new WindowRuleEditorModel());

        SetCell(row, "Name", $"{window.ProcessName} - {window.WindowTitle}");
        SetCell(row, "ProcessName", window.ProcessName);
        SetCell(row, "WindowTitle", window.WindowTitle);
        SetCell(row, "MatchMode", WindowTitleMatchMode.Exact.ToString());
        SetCell(row, "BlockAction", WindowBlockAction.Close.ToString());
        SetCell(row, "AllowedRoles", "superadmin, Tecnico QA");

        SetResult($"Ventana seleccionada: {window.ProcessName}.exe / {window.WindowTitle}", Color.Green);
    }

    private static string GetCellString(DataGridViewRow row, string columnName)
        => row.Cells[columnName].Value?.ToString()?.Trim() ?? string.Empty;

    private static void SetCell(DataGridViewRow row, string columnName, string value)
        => row.Cells[columnName].Value = value;

    private static string JsonString(JsonObject? obj, string propertyName, string fallback = "")
    {
        try
        {
            return obj?[propertyName]?.GetValue<string>()?.Trim() ?? fallback;
        }
        catch
        {
            return obj?[propertyName]?.ToString()?.Trim() ?? fallback;
        }
    }

    private static bool JsonBool(JsonObject? obj, string propertyName, bool fallback)
    {
        try
        {
            return obj?[propertyName]?.GetValue<bool>() ?? fallback;
        }
        catch
        {
            var text = obj?[propertyName]?.ToString();
            return bool.TryParse(text, out var value) ? value : fallback;
        }
    }

    private static int JsonInt(JsonObject? obj, string propertyName, int fallback)
    {
        try
        {
            return obj?[propertyName]?.GetValue<int>() ?? fallback;
        }
        catch
        {
            var text = obj?[propertyName]?.ToString();
            return int.TryParse(text, out var value) ? value : fallback;
        }
    }

    private static string JsonStringList(JsonObject obj, string propertyName)
    {
        if (obj[propertyName] is JsonArray array)
        {
            return string.Join(", ",
                array.Select(item => item?.ToString()?.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        return JsonString(obj, propertyName);
    }

    private static JsonArray SplitListToJsonArray(string value)
    {
        var array = new JsonArray();
        foreach (var item in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(item))
                array.Add(item);
        }
        return array;
    }

    private static string NormalizeEnumName<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed.ToString()
            : fallback.ToString();

    private static JsonNode? DefaultWindowAccessGuardJson()
        => JsonNode.Parse("""
        {
          "Enabled": true,
          "PollMilliseconds": 500,
          "Rules": [
            {
              "Name": "AT-01 Error View",
              "ProcessName": "inctest",
              "WindowTitle": "Error View",
              "MatchMode": "Exact",
              "BlockAction": "Close",
              "AllowedRoles": [ "superadmin", "Tecnico QA" ],
              "AllowedUsers": []
            }
          ]
        }
        """);

    private static JsonNode? DefaultQrInputFocusJson()
        => JsonNode.Parse("""
        {
          "Enabled": false,
          "DelayMilliseconds": 500,
          "RetryCount": 3,
          "ProcessName": "",
          "WindowTitle": "",
          "MatchMode": "Exact",
          "Input": {
            "AutomationId": "",
            "Name": "",
            "ClassName": "",
            "ControlType": "",
            "ControlIndex": 0,
            "NativeWindowHandle": 0,
            "UseFallbackClick": false,
            "FallbackClickX": -1,
            "FallbackClickY": -1
          }
        }
        """);

    private sealed class WindowRuleEditorModel
    {
        public string Name { get; init; } = "";
        public string ProcessName { get; init; } = "";
        public string WindowTitle { get; init; } = "";
        public string MatchMode { get; init; } = WindowTitleMatchMode.Exact.ToString();
        public string BlockAction { get; init; } = WindowBlockAction.Close.ToString();
        public string AllowedRoles { get; init; } = "superadmin, Tecnico QA";
        public string AllowedUsers { get; init; } = "";
    }

    private sealed record WindowCandidate(string ProcessName, string WindowTitle)
    {
        public override string ToString() => $"{ProcessName}.exe - {WindowTitle}";
    }


    // ── UI factory helpers ────────────────────────────────────

    private static Label MakeLabel(string text, int x, int y) =>
        new()
        {
            AutoSize = false,
            Location = new Point(x, y),
            Size = new Size(160, 22),
            Text = text,
            ForeColor = Branding.Ink,
            BackColor = Color.Transparent
        };

    private static Label MakeHint(string text, int x, int y) =>
        new()
        {
            AutoSize = false,
            Location = new Point(x, y),
            Size = new Size(320, 22),
            Text = text,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            ForeColor = Color.Gray,
            BackColor = Color.Transparent
        };

    private TextBox MakeTextBox(int x, int y, int width) =>
        new()
        {
            Location = new Point(x, y),
            Width = width,
            Height = 28,
            Font = Font,
            BorderStyle = BorderStyle.FixedSingle
        };

    private FlatRoundButton MakeButton(string text, int x, int y, int width, Color backColor) =>
        new()
        {
            Text = text,
            Location = new Point(x, y),
            Width = width,
            Height = 40,
            BaseColor = backColor
        };
}
