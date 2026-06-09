namespace QualityLock.Client.WinForms.Forms;

/// <summary>
/// Indicador persistente que muestra el usuario logueado y su rol mientras la sesion
/// esta activa. Caracteristicas:
///   - Siempre visible (TopMost), no aparece en la barra de tareas.
///   - Arrastrable: el operador puede moverlo a cualquier parte de la pantalla.
///   - Tiene un boton "Cerrar sesion" que vuelve a bloquear la estacion.
///   - No se puede cerrar como ventana (sin borde ni boton X); solo el LockForm lo
///     oculta cuando la sesion termina.
/// </summary>
public class SessionBadgeForm : Form
{
    private readonly Label _lblUser;
    private readonly Label _lblRole;
    private readonly Label _lblTime;
    private readonly Button _btnLock;
    private readonly System.Windows.Forms.Timer _timer;
    private DateTime _sessionStart;

    /// <summary>Se invoca cuando el operador pulsa "Cerrar sesion".</summary>
    public event Action? LockRequested;

    // Arrastre manual (la ventana no tiene barra de titulo).
    private bool _dragging;
    private Point _dragOffset;

    public SessionBadgeForm()
    {
        FormBorderStyle = FormBorderStyle.None;     // sin barra ni boton de cerrar
        StartPosition   = FormStartPosition.Manual;
        TopMost         = true;
        ShowInTaskbar   = false;
        Width           = 340;
        Height          = 116;
        BackColor       = Color.FromArgb(30, 120, 30);
        ControlBox      = false;

        _lblUser = new Label
        {
            AutoSize  = false,
            Left = 14, Top = 8, Width = 312, Height = 26,
            Font      = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _lblRole = new Label
        {
            AutoSize  = false,
            Left = 14, Top = 34, Width = 190, Height = 22,
            Font      = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(210, 245, 210),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Tiempo de sesión, a la derecha, con un icono de reloj.
        _lblTime = new Label
        {
            AutoSize  = false,
            Left = 196, Top = 34, Width = 130, Height = 22,
            Font      = new Font("Consolas", 10, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleRight,
            Text      = "⏱ 00:00:00"
        };

        _btnLock = new FlatRoundButton
        {
            Text      = "🔒  Cerrar sesión",
            Left = 14, Top = 62, Width = 312, Height = 30,
            BaseColor = Color.FromArgb(18, 95, 40),
            SurfaceColor = Color.FromArgb(30, 120, 30),   // = fondo verde del badge
            CornerRadius = 6,
            Font      = new Font("Segoe UI Semibold", 9, FontStyle.Bold),
            TabStop   = false
        };
        _btnLock.Click += (_, _) => LockRequested?.Invoke();

        Controls.Add(_lblUser);
        Controls.Add(_lblRole);
        Controls.Add(_lblTime);
        Controls.Add(_btnLock);

        // Cronómetro de sesión: actualiza el tiempo transcurrido cada segundo.
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _sessionStart;
            _lblTime.Text = $"⏱ {(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        };

        // Arrastre: el fondo y las etiquetas mueven la ventana (el boton NO, para no
        // disparar el cierre de sesion accidentalmente al moverla).
        foreach (Control c in new Control[] { this, _lblUser, _lblRole, _lblTime })
        {
            c.MouseDown += OnDragStart;
            c.MouseMove += OnDragMove;
            c.MouseUp   += (_, _) => _dragging = false;
        }
    }

    /// <summary>Actualiza el contenido (usuario + rol), reinicia el cronómetro y posiciona.</summary>
    public void ShowSession(string displayName, string? role)
    {
        _lblUser.Text = $"✔  {displayName}";
        _lblRole.Text = string.IsNullOrWhiteSpace(role) ? "Sesión activa" : $"Rol: {role}";

        // Reinicia el cronómetro al inicio de la sesión.
        _sessionStart = DateTime.UtcNow;
        _lblTime.Text = "⏱ 00:00:00";
        _timer.Start();

        // Posicion por defecto: esquina inferior derecha, solo si aun no se ha movido.
        if (Location.IsEmpty || (Location.X == 0 && Location.Y == 0))
        {
            var wa = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        }

        if (!Visible) Show();
        TopMost = true;
        BringToFront();
    }

    /// <summary>Detiene el cronómetro (la sesión terminó / el indicador se oculta).</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible) _timer.Stop();
        base.OnVisibleChanged(e);
    }

    private void OnDragStart(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        var screenPt = (sender is Control c) ? c.PointToScreen(e.Location) : e.Location;
        _dragOffset = new Point(screenPt.X - Left, screenPt.Y - Top);
    }

    private void OnDragMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var screenPt = (sender is Control c) ? c.PointToScreen(e.Location) : e.Location;
        Location = new Point(screenPt.X - _dragOffset.X, screenPt.Y - _dragOffset.Y);
    }

    /// <summary>
    /// Impide que el usuario cierre la ventana (Alt+F4, etc.). Solo se cierra cuando la
    /// aplicacion termina (CloseReason.ApplicationExitCall / WindowsShutDown).
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.UserClosing or CloseReason.None)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }
}
