using QualityLock.Client.WinForms.Services;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;
using QualityLock.Shared.Input;

namespace QualityLock.Client.WinForms.Forms;

/// <summary>
/// Overlay a pantalla completa que pide la autorización de alguien con permisos para
/// revelar una ventana restringida (acción <c>PromptAuthorization</c> del guard).
/// Acepta dos vías: escanear el gafete (campo oculto + detector de velocidad) o teclear
/// usuario + contraseña. Valida contra <c>usuarios_sistema</c> (vía API, con respaldo de
/// caché offline para el escaneo) y comprueba el rol contra los <c>AllowedRoles</c> de la
/// regla que disparó el bloqueo.
/// </summary>
public static class WindowAuthorizationOverlay
{
    private static readonly Color BgRed = Color.FromArgb(140, 30, 30);
    private static readonly Color BgRedDark = Color.FromArgb(110, 22, 22);

    public static AuthorizationResult RequestAuthorization(
        AuthorizationRequest req,
        ApiClientService api,
        LocalStateService localState,
        string stationCode,
        int scanMaxAvgKeyMs,
        bool allowManualLogin = true)
    {
        using var dlg = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            WindowState     = FormWindowState.Maximized,
            StartPosition   = FormStartPosition.CenterScreen,
            TopMost         = true,
            BackColor       = BgRed,
            Icon            = Branding.AppIcon()
        };

        var lblTitle = new Label
        {
            Text = "Se requiere autorización",
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top,
            Height = 70, ForeColor = Color.White, Font = new Font("Segoe UI", 26, FontStyle.Bold)
        };
        var lblSub = new Label
        {
            Text = $"Ventana restringida: {req.WindowTitle}",
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top,
            Height = 36, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 13)
        };
        var lblHint = new Label
        {
            Text = allowManualLogin
                ? "Escanee el gafete de un autorizador, o teclee usuario y contraseña."
                : "Escanee el gafete de un autorizador.",
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top,
            Height = 32, ForeColor = Color.White, Font = new Font("Segoe UI", 12)
        };
        var lblStatus = new Label
        {
            Text = "", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top,
            Height = 36, ForeColor = Color.Gold, Font = new Font("Segoe UI", 13, FontStyle.Bold)
        };

        // ── Tarjeta central con usuario + contraseña ──
        var card = new Panel { Width = 420, Height = 230, BackColor = BgRedDark, Anchor = AnchorStyles.None };

        var lblUser = new Label
        {
            Text = "Usuario", Left = 30, Top = 20, AutoSize = true,
            ForeColor = Color.White, Font = new Font("Segoe UI", 11)
        };
        var txtUser = new TextBox
        {
            Left = 30, Top = 46, Width = 360, Height = 30,
            Font = new Font("Segoe UI", 12), BorderStyle = BorderStyle.FixedSingle
        };
        var lblPass = new Label
        {
            Text = "Contraseña", Left = 30, Top = 86, AutoSize = true,
            ForeColor = Color.White, Font = new Font("Segoe UI", 11)
        };
        var txtPass = new TextBox
        {
            Left = 30, Top = 112, Width = 360, Height = 30, UseSystemPasswordChar = true,
            Font = new Font("Segoe UI", 12), BorderStyle = BorderStyle.FixedSingle
        };
        var btnAuth = new FlatRoundButton
        {
            Text = "Autorizar", Left = 30, Top = 158, Width = 360, Height = 44,
            BaseColor = Color.FromArgb(30, 140, 60), SurfaceColor = BgRedDark // esquinas = color de la tarjeta
        };
        card.Controls.AddRange([lblUser, txtUser, lblPass, txtPass, btnAuth]);

        var btnCancel = new FlatRoundButton
        {
            Text = "Cancelar", Width = 200, Height = 44, BaseColor = Color.FromArgb(70, 70, 70),
            SurfaceColor = BgRed, // esquinas = color del fondo del overlay
            Anchor = AnchorStyles.None
        };

        // Campo de captura del escáner: invisible pero enfocable (la estación está
        // desbloqueada, así que no podemos reusar el _txtBadge de la lock screen).
        var txtBadge = new TextBox { Width = 1, Height = 1, BorderStyle = BorderStyle.None, Top = 0, Left = 0 };

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        if (allowManualLogin) center.Controls.Add(card);
        center.Controls.Add(btnCancel);
        center.Resize += (_, _) =>
        {
            card.Location = new Point((center.Width - card.Width) / 2, (center.Height / 2) - 150);
            // Sin vía manual la tarjeta no está; el botón Cancelar se centra solo.
            var cancelTop = allowManualLogin ? card.Bottom + 24 : (center.Height / 2);
            btnCancel.Location = new Point((center.Width - btnCancel.Width) / 2, cancelTop);
        };

        dlg.Controls.Add(txtBadge);
        dlg.Controls.Add(center);
        dlg.Controls.Add(lblStatus);
        dlg.Controls.Add(lblHint);
        dlg.Controls.Add(lblSub);
        dlg.Controls.Add(lblTitle);
        center.BringToFront();

        var detector = new ScanSpeedDetector(scanMaxAvgKeyMs);
        var processing = false;
        AuthorizationResult result = new(false, null, null, null);

        // El escáner enfoca el campo oculto al iniciar; el autorizador puede hacer clic
        // en "Usuario" para teclear. RecordKey solo cuenta en el campo oculto del escáner.
        dlg.Shown += (_, _) => { dlg.Activate(); txtBadge.Focus(); };

        btnCancel.Click += (_, _) => dlg.Close(); // result queda en (false, ...)

        void ShowError(string msg)
        {
            lblStatus.ForeColor = Color.Gold;
            lblStatus.Text = msg;
        }

        void Succeed(string badge, string? displayName, string? role)
        {
            result = new AuthorizationResult(true, badge, displayName, role);
            dlg.DialogResult = DialogResult.OK; // cierra el overlay
        }

        // ── Vía 1: escaneo de gafete ──
        txtBadge.KeyPress += (_, ev) =>
        {
            if (!char.IsControl(ev.KeyChar))
                detector.RecordKey();
        };
        txtBadge.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;

            var badge = txtBadge.Text.Trim();
            var cameFromScanner = detector.LooksLikeScan();
            txtBadge.Clear();
            detector.Reset();

            if (string.IsNullOrEmpty(badge) || processing) return;

            // Si lo tecleado no parece escaneo, dirigir al campo de usuario manual.
            if (!cameFromScanner)
            {
                txtUser.Text = badge;
                txtPass.Focus();
                return;
            }

            processing = true;
            ShowError("Validando…");
            try
            {
                var (valid, displayName, role) = await ValidateBadgeAsync(badge, api, localState, stationCode);
                if (valid && req.Rule.Allows(badge, role))
                    Succeed(badge, displayName, role);
                else
                    ShowError(valid ? "Su rol no está autorizado para esta ventana." : "Gafete no reconocido.");
            }
            catch { ShowError("Error al validar. Intente de nuevo."); }
            finally
            {
                processing = false;
                if (!dlg.IsDisposed) txtBadge.Focus();
            }
        };

        // ── Vía 2: usuario + contraseña ──
        async Task AuthorizeManualAsync()
        {
            var username = txtUser.Text.Trim();
            var password = txtPass.Text;
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || processing)
            {
                ShowError("Ingrese usuario y contraseña.");
                return;
            }

            processing = true;
            btnAuth.Enabled = false;
            ShowError("Validando…");
            try
            {
                if (!await api.IsAvailableAsync())
                {
                    ShowError("Sin conexión: el acceso manual requiere servidor. Use el escáner.");
                    return;
                }

                var login = await api.UserLoginAsync(new AdminLoginRequest(username, password));
                if (login is not { Authenticated: true })
                {
                    ShowError("Usuario o contraseña incorrectos.");
                    return;
                }

                // El autorizador manual se compara por rol contra la regla (badge null:
                // Allows admite por rol). AllowedUsers no aplica a la vía manual.
                if (req.Rule.Allows(null, login.Role))
                    Succeed(login.Username, login.DisplayName, login.Role);
                else
                    ShowError("Su rol no está autorizado para esta ventana.");
            }
            catch { ShowError("Error al validar. Intente de nuevo."); }
            finally
            {
                processing = false;
                if (!dlg.IsDisposed) { btnAuth.Enabled = true; txtPass.SelectAll(); txtPass.Focus(); }
            }
        }

        btnAuth.Click += async (_, _) => await AuthorizeManualAsync();
        // Enter en el campo contraseña confirma, sin tener que dar clic.
        txtPass.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await AuthorizeManualAsync();
        };

        dlg.ShowDialog();
        return result;
    }

    /// <summary>Valida el gafete online (API) o, sin conexión, contra la caché local.</summary>
    private static async Task<(bool Valid, string? DisplayName, string? Role)> ValidateBadgeAsync(
        string badge, ApiClientService api, LocalStateService localState, string stationCode)
    {
        if (await api.IsAvailableAsync())
        {
            var r = await api.ValidateBadgeAsync(
                new BadgeValidationRequest(stationCode, badge, DateTime.UtcNow, Line: api.Line));
            return r?.Decision == ValidationDecision.Allowed ? (true, r.DisplayName, r.Role) : (false, null, null);
        }

        var cached = localState.LoadOperatorCache()
            .FirstOrDefault(o => string.Equals(o.BadgeCode, badge, StringComparison.OrdinalIgnoreCase));
        return cached is not null ? (true, cached.DisplayName, cached.Role) : (false, null, null);
    }
}

public sealed record AuthorizationResult(bool Authorized, string? BadgeCode, string? DisplayName, string? Role);
