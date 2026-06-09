namespace QualityLock.Client.WinForms.Services;

/// <summary>
/// Carga y cachea los recursos de marca (logo) desde el archivo LOGO.png que se copia
/// junto al ejecutable. Falla silenciosamente (devuelve null) si el archivo no existe,
/// para que la app no se rompa por un recurso de imagen faltante.
/// </summary>
public static class Branding
{
    public static readonly Color Green = Color.FromArgb(20, 150, 60);
    public static readonly Color GreenDark = Color.FromArgb(15, 110, 45);
    public static readonly Color Ink = Color.FromArgb(40, 50, 60);
    public static readonly Color Panel = Color.FromArgb(247, 249, 248);

    private static Image? _logo;
    private static bool _logoTried;

    /// <summary>Logo de la app (LOGO.png junto al exe). Null si no se encuentra.</summary>
    public static Image? Logo
    {
        get
        {
            if (!_logoTried)
            {
                _logoTried = true;
                try
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "LOGO.png");
                    if (File.Exists(path))
                        _logo = Image.FromFile(path);
                }
                catch { _logo = null; }
            }
            return _logo;
        }
    }

    /// <summary>Icono de ventana derivado del logo (32x32). Null si no hay logo.</summary>
    public static Icon? AppIcon()
    {
        try
        {
            if (Logo is null) return null;
            using var bmp = new Bitmap(Logo, new Size(32, 32));
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch { return null; }
    }
}
