using QualityLock.Client.WinForms.Services;

namespace QualityLock.Client.WinForms.Forms;

/// <summary>
/// Diálogo modal de acceso de administrador: pide usuario + contraseña y los valida
/// contra <c>usuarios_sistema</c> (vía API) con respaldo de PIN local offline.
/// </summary>
public static class AdminLoginPrompt
{
    /// <summary>
    /// Muestra el diálogo y devuelve el resultado de autenticación, o <c>null</c> si el
    /// usuario canceló. El campo "Usuario" puede quedar vacío cuando se usa el PIN local
    /// (offline).
    /// </summary>
    public static async Task<AdminAuthResult?> AuthenticateAsync(AdminPinService adminPin, IWin32Window? owner)
    {
        using var dlg = new Form
        {
            Text            = "QualityLock — Acceso de administrador",
            Width           = 420,
            Height          = 320,
            StartPosition   = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox     = false,
            MinimizeBox     = false,
            TopMost         = true,
            BackColor       = Color.White,
            Icon            = Branding.AppIcon()
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
            Text = "Acceso de administrador", Location = new Point(70, 18), AutoSize = true,
            Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Branding.GreenDark
        };
        var accent = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Branding.Green };
        header.Controls.AddRange([logoBox, lblTitle, accent]);

        var lblUser = new Label { Text = "Usuario (opcional si usa el PIN):", Top = 78, Left = 20, AutoSize = true, ForeColor = Branding.Ink };
        var txtUser = new TextBox { Top = 100, Left = 20, Width = 364, Height = 28, Font = new Font("Segoe UI", 11), BorderStyle = BorderStyle.FixedSingle };

        var lblPass = new Label { Text = "Contraseña o PIN local:", Top = 136, Left = 20, AutoSize = true, ForeColor = Branding.Ink };
        var txtPass = new TextBox { Top = 158, Left = 20, Width = 364, Height = 28, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 11), BorderStyle = BorderStyle.FixedSingle };

        var btn = new FlatRoundButton { Text = "Confirmar", Top = 200, Left = 20, Width = 364, Height = 40, BaseColor = Branding.Green };
        var lblStatus = new Label { Top = 248, Left = 20, Width = 364, Height = 36, ForeColor = Color.Gray, Font = new Font("Segoe UI", 9) };

        dlg.Controls.AddRange([lblUser, txtUser, lblPass, txtPass, btn, lblStatus, header]);
        dlg.AcceptButton = btn;
        dlg.Shown += (_, _) => { dlg.Activate(); txtUser.Focus(); };

        AdminAuthResult? result = null;

        // Un solo handler: valida sin cerrar para poder mostrar el error y reintentar.
        // El diálogo solo se cierra con DialogResult.OK cuando la autenticación tiene éxito.
        btn.Click += async (_, _) =>
        {
            btn.Enabled = false;
            lblStatus.ForeColor = Color.Gray;
            lblStatus.Text = "Verificando...";

            var r = await adminPin.AuthenticateAsync(txtUser.Text, txtPass.Text);
            if (r.Authenticated)
            {
                result = r;
                dlg.DialogResult = DialogResult.OK;   // cierra el diálogo
                return;
            }

            lblStatus.ForeColor = Color.Firebrick;
            lblStatus.Text = r.Message;
            txtPass.SelectAll();
            txtPass.Focus();
            btn.Enabled = true;
        };

        _ = owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner);

        // result queda en null si el usuario canceló (cerró la ventana sin éxito).
        return result;
    }
}
