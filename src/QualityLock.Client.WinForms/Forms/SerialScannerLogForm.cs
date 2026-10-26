using QualityLock.Client.WinForms.Services;

namespace QualityLock.Client.WinForms.Forms;

/// <summary>Estado y log en vivo del escaner por puerto COM (se abre desde la bandeja).</summary>
public sealed class SerialScannerLogForm : Form
{
    private readonly SerialScannerService _scanner;
    private readonly Label _lblStatus;
    private readonly TextBox _txtLog;

    public SerialScannerLogForm(SerialScannerService scanner)
    {
        _scanner = scanner;

        Text = "QualityLock — Escáner COM";
        Icon = Branding.AppIcon();
        Size = new Size(620, 420);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Font = new Font("Segoe UI", 10);

        _lblStatus = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(10, 8, 0, 0),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5f)
        };
        Controls.Add(_txtLog);
        Controls.Add(_lblStatus);

        var recent = _scanner.RecentLog;
        _txtLog.Text = recent.Count == 0 ? "" : string.Join(Environment.NewLine, recent) + Environment.NewLine;
        RefreshStatus();

        _scanner.LogAdded += Scanner_LogAdded;
        FormClosed += (_, _) => _scanner.LogAdded -= Scanner_LogAdded;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    // Llega desde cualquier hilo (puerto serie, timer de reconexion).
    private void Scanner_LogAdded(string line)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            _txtLog.AppendText(line + Environment.NewLine);
            RefreshStatus();
        });
    }

    private void RefreshStatus()
    {
        var status = _scanner.Status;
        _lblStatus.Text = (status.StartsWith("Conectado") ? "● " : "○ ") + status;
        _lblStatus.ForeColor = status.StartsWith("Conectado") ? Branding.GreenDark : Color.Gray;
    }
}
