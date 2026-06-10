using QualityLock.Client.WinForms.Services;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;

namespace QualityLock.Client.WinForms.Forms;

public class AdminPanelForm : Form
{
    private readonly string _stationCode;
    private readonly ApiClientService _api;
    private readonly BypassService _bypass;

    private TextBox _txtAdminBadge = null!;
    private ComboBox _cmbReason = null!;
    private TextBox _txtComments = null!;
    private Button _btnApply = null!;
    private Label _lblResult = null!;

    /// <summary>True when an offline local bypass was validated and the caller should unlock.</summary>
    public bool BypassGranted { get; private set; }

    /// <summary>The reason carried by the bypass token (for audit), when granted.</summary>
    public string BypassReason { get; private set; } = string.Empty;

    public AdminPanelForm(string stationCode, ApiClientService api, BypassService bypass)
    {
        _stationCode = stationCode;
        _api = api;
        _bypass = bypass;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Panel de Administración — QualityLock";
        Width = 500;
        Height = 400;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var lblAdminBadge = new Label { Text = "Gafete Admin:", Top = 20, Left = 20, Width = 120 };
        _txtAdminBadge = new TextBox { Top = 20, Left = 150, Width = 300 };

        var lblReason = new Label { Text = "Motivo:", Top = 60, Left = 20, Width = 120 };
        _cmbReason = new ComboBox
        {
            Top = 60, Left = 150, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbReason.Items.AddRange(Enum.GetNames<OverrideReasonType>());
        _cmbReason.SelectedIndex = 0;

        var lblComments = new Label { Text = "Comentarios:", Top = 100, Left = 20, Width = 120 };
        _txtComments = new TextBox { Top = 100, Left = 150, Width = 300, Height = 80, Multiline = true };

        _btnApply = new Button
        {
            Text = "Aplicar Override",
            Top = 200, Left = 150, Width = 160, Height = 36
        };
        _btnApply.Click += BtnApply_Click;

        _lblResult = new Label
        {
            Top = 250, Left = 20, Width = 440, Height = 60,
            Font = new Font("Segoe UI", 11)
        };

        Controls.AddRange([
            lblAdminBadge, _txtAdminBadge,
            lblReason, _cmbReason,
            lblComments, _txtComments,
            _btnApply, _lblResult
        ]);
    }

    private async void BtnApply_Click(object? sender, EventArgs e)
    {
        _btnApply.Enabled = false;
        _lblResult.Text = "Procesando...";

        var isOnline = await _api.IsAvailableAsync();

        if (!isOnline)
        {
            var (valid, reason) = _bypass.ValidateBypass(_stationCode);
            _lblResult.ForeColor = valid ? Color.Green : Color.Red;
            _lblResult.Text = valid
                ? $"BYPASS LOCAL APLICADO: {reason}"
                : $"Backend no disponible y bypass inválido: {reason}";

            if (valid)
            {
                // Signal the lock screen to unlock and audit the bypass.
                BypassGranted = true;
                BypassReason = reason;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _btnApply.Enabled = true;
            return;
        }

        var reasonType = Enum.Parse<OverrideReasonType>(_cmbReason.SelectedItem!.ToString()!);
        var result = await _api.AdminOverrideAsync(new AdminOverrideRequest(
            _stationCode,
            _txtAdminBadge.Text.Trim(),
            null,
            reasonType,
            _txtComments.Text.Trim(),
            DateTime.UtcNow,
            Line: _api.Line));

        _lblResult.ForeColor = result?.Approved == true ? Color.Green : Color.Red;
        _lblResult.Text = result?.Message ?? "Sin respuesta del servidor.";
        _btnApply.Enabled = true;
    }
}
