using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace QualityLock.Client.WinForms.Forms;

/// <summary>
/// Botón con esquinas redondeadas suaves (antialiasing), inmune al tema del sistema
/// (claro/oscuro). Se renderiza a un bitmap: las esquinas se pintan con el color del
/// contenedor (<see cref="SurfaceColor"/>), por lo que nunca se ven negras. Estados
/// normal / hover / pressed.
/// </summary>
public sealed class FlatRoundButton : Button
{
    private bool _hover;
    private bool _down;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BaseColor { get; set; } = Color.FromArgb(20, 150, 60);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 8;

    /// <summary>Color de la superficie sobre la que se apoya el botón (esquinas). Blanco
    /// por defecto (sobre las tarjetas blancas). NO depende del tema del sistema.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor { get; set; } = Color.White;

    public FlatRoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Height = 40;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;

        var color = BaseColor;
        if (!Enabled) color = Color.FromArgb(195, 200, 198);
        else if (_down) color = Darken(BaseColor, 0.16f);
        else if (_hover) color = Lighten(BaseColor, 0.10f);

        // Render a bitmap: controlamos cada pixel. Las esquinas se rellenan con
        // SurfaceColor (no con el tema del sistema), por eso nunca se ven negras.
        using var bmp = new Bitmap(Width, Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var bg = new SolidBrush(SurfaceColor))
                g.FillRectangle(bg, 0, 0, Width, Height);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var path = Rounded(rect, CornerRadius);
            using (var fill = new SolidBrush(color))
                g.FillPath(fill, path);

            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        e.Graphics.DrawImageUnscaled(bmp, 0, 0);
    }

    private static GraphicsPath Rounded(RectangleF r, int radius)
    {
        float d = Math.Max(1, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Color Lighten(Color c, float f) => Color.FromArgb(
        c.R + (int)((255 - c.R) * f), c.G + (int)((255 - c.G) * f), c.B + (int)((255 - c.B) * f));

    private static Color Darken(Color c, float f) => Color.FromArgb(
        (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
}
