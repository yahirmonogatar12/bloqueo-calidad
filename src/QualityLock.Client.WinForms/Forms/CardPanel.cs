using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace QualityLock.Client.WinForms.Forms;

/// <summary>
/// Panel "tarjeta" de sección: fondo blanco con esquinas redondeadas suaves y borde
/// sutil. Pinta el fondo del contenedor en las esquinas para que no se vean negras.
/// </summary>
public sealed class CardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(220, 226, 223);

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Color del contenedor en las esquinas (sube por padres hasta uno opaco).
        var corner = Color.FromArgb(247, 249, 248);
        var p = Parent;
        while (p is not null)
        {
            if (p.BackColor.A == 255 && p.BackColor != Color.Transparent) { corner = p.BackColor; break; }
            p = p.Parent;
        }
        using (var bg = new SolidBrush(corner))
            g.FillRectangle(bg, ClientRectangle);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Rounded(rect, CornerRadius);
        using (var fill = new SolidBrush(BackColor))
            g.FillPath(fill, path);
        using (var pen = new Pen(BorderColor, 1))
            g.DrawPath(pen, path);
    }

    private static GraphicsPath Rounded(RectangleF r, int radius)
    {
        float d = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
