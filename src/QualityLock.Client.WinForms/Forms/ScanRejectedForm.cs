using QualityLock.Shared.DTOs;

namespace QualityLock.Client.WinForms.Forms;

/// <summary>
/// Aviso rojo de escaneo rechazado por la verificacion MES. No roba el foco (el cursor del
/// operador sigue en el campo de la prueba) y se quita solo.
/// </summary>
public sealed class ScanRejectedForm : Form
{
    private const int VisibleMilliseconds = 4000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly Label _lblTitle;
    private readonly Label _lblCode;
    private readonly Label _lblReason;
    private readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = VisibleMilliseconds };

    // NG.wav va dentro del exe (EmbeddedResource). Static: se carga una vez y el
    // reproductor sigue vivo mientras suena.
    private static readonly System.Media.SoundPlayer? NgSound = LoadNgSound();

    private static System.Media.SoundPlayer? LoadNgSound()
    {
        try
        {
            var stream = typeof(ScanRejectedForm).Assembly.GetManifestResourceStream("QualityLock.NG.wav");
            if (stream is null) return null;
            var player = new System.Media.SoundPlayer(stream);
            player.Load();
            return player;
        }
        catch
        {
            return null;
        }
    }

    public ScanRejectedForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(190, 20, 20);
        Size = new Size(760, 200);

        _lblTitle = new Label
        {
            Dock = DockStyle.Top, Height = 60, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Color.White
        };
        _lblCode = new Label
        {
            Dock = DockStyle.Top, Height = 50, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true,
            Font = new Font("Consolas", 16, FontStyle.Bold), ForeColor = Color.White
        };
        _lblReason = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true,
            Font = new Font("Segoe UI", 14), ForeColor = Color.White
        };
        Controls.Add(_lblReason);
        Controls.Add(_lblCode);
        Controls.Add(_lblTitle);

        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        foreach (Control c in Controls) c.Click += (_, _) => Hide();
    }

    // TopMost por CreateParams y no por la propiedad: la propiedad puede activar la ventana.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>Titulo del aviso segun el motivo. check null = el MES no respondio.</summary>
    public static string TitleFor(string code, VisionCheckResponse? check) => check switch
    {
        null => "⛔  SIN CONEXIÓN CON MES",
        { LastResult: not null } => "⛔  PIEZA NG EN VISIÓN",
        _ when !code.StartsWith("EBR", StringComparison.OrdinalIgnoreCase) => "⛔  NO ES UN BARCODE EBR",
        _ => "⛔  PIEZA NO PASÓ POR VISIÓN",
    };

    public void ShowRejection(string title, string code, string reason)
    {
        _lblTitle.Text = title;
        _lblCode.Text = code;
        _lblReason.Text = reason;

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + area.Height / 5);

        if (!Visible) Show();
        _hideTimer.Stop();
        _hideTimer.Start();
        if (NgSound is not null) NgSound.Play();
        else System.Media.SystemSounds.Hand.Play();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hideTimer.Dispose();
        base.Dispose(disposing);
    }
}
