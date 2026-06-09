using Microsoft.Extensions.Configuration;
using QualityLock.Client.WinForms.Services;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;

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
    private NumericUpDown _numAutoLock = null!;
    private CheckBox _chkRequireScan = null!;
    private NumericUpDown _numScanMaxMs = null!;
    private TextBox _txtAdminPin = null!;

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
        bodyStation.Controls.Add(MakeLabel("Hostname:", LabelX, sy + 2));
        _txtHostName = MakeTextBox(FieldX, sy, 280);
        _txtHostName.ReadOnly = true;
        _txtHostName.BackColor = Color.FromArgb(238, 240, 239);
        bodyStation.Controls.Add(_txtHostName); sy += 38;
        _chkActive = new CheckBox
        {
            Text = "Estación activa", Location = new Point(FieldX, sy), AutoSize = true, Checked = true, Font = Font
        };
        bodyStation.Controls.Add(_chkActive);
        scroll.Controls.Add(cardStation);

        // ── Tarjeta: Seguridad y bloqueo ──
        var cardSec = MakeCard("Seguridad y bloqueo", out var bodySec, 168);
        int qy = 6;
        bodySec.Controls.Add(MakeLabel("Inactividad (min):", LabelX, qy + 2));
        _numAutoLock = MakeNumeric(FieldX, qy, 1, 1440, 5, 1);
        bodySec.Controls.Add(_numAutoLock);
        bodySec.Controls.Add(MakeHint("Minutos sin mouse/teclado antes de bloquear.", FieldX + 100, qy + 2));
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
            var config = new ConfigurationBuilder()
                .AddJsonFile(_configPath, optional: true)
                .Build();

            _txtApiUrl.Text = config["ApiBaseUrl"] ?? _api.BaseAddress;
            _txtStationCode.Text = config["StationCode"] ?? string.Empty;
            _txtHostName.Text = Environment.MachineName;

            // Pre-fill station name if code is set
            if (!string.IsNullOrEmpty(_txtStationCode.Text))
                _txtStationName.Text = _txtStationCode.Text;

            // Seguridad / bloqueo. AutoLockSeconds se guarda en SEGUNDOS, pero el campo
            // se muestra en MINUTOS (redondeando hacia arriba al minuto mas cercano).
            if (int.TryParse(config["AutoLockSeconds"], out var sec) && sec > 0)
            {
                var minutes = Math.Max(1, (int)Math.Round(sec / 60.0));
                if (minutes <= _numAutoLock.Maximum) _numAutoLock.Value = minutes;
            }
            _chkRequireScan.Checked = !string.Equals(config["RequireScan"], "false", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(config["ScanMaxAvgKeyMs"], out var ms) && ms >= _numScanMaxMs.Minimum && ms <= _numScanMaxMs.Maximum)
                _numScanMaxMs.Value = ms;
            _txtAdminPin.Text = config["AdminPin"] ?? string.Empty;
        }
        catch
        {
            _txtApiUrl.Text = _api.BaseAddress;
            _txtHostName.Text = Environment.MachineName;
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

        var request = new RegisterStationRequest(
            _txtStationCode.Text.Trim(),
            _txtStationName.Text.Trim(),
            Enum.Parse<StationType>(_cmbStationType.SelectedItem!.ToString()!),
            Environment.MachineName,
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
            var existing = new ConfigurationBuilder()
                .AddJsonFile(_configPath, optional: true)
                .Build();

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                StationCode = _txtStationCode.Text.Trim(),
                ApiBaseUrl = _txtApiUrl.Text.Trim().TrimEnd('/') + "/",
                BypassHmacSecret = existing["BypassHmacSecret"] ?? string.Empty,
                AdminPin = _txtAdminPin.Text.Trim(),
                ClientApiKey = existing["ClientApiKey"] ?? string.Empty,
                AutoLockSeconds = (int)_numAutoLock.Value * 60,   // minutos (UI) -> segundos
                RequireScan = _chkRequireScan.Checked,
                ScanMaxAvgKeyMs = (int)_numScanMaxMs.Value
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_configPath, json);
            SetResult("✔  Configuración guardada.", Color.Green);

            MessageBox.Show(
                "La configuración se guardó correctamente.\n\n" +
                "Los cambios (inactividad, escáner, URL, etc.) se aplican al REINICIAR " +
                "la pantalla de bloqueo. Use «Detener servicio» y vuelva a iniciarla, " +
                "o reinicie la estación.",
                "Reinicie para aplicar",
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
            DialogResult = DialogResult.Cancel;
            Application.Exit();
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
