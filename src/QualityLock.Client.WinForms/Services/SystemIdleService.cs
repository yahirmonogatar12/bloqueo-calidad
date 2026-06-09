using System.Runtime.InteropServices;

namespace QualityLock.Client.WinForms.Services;

/// <summary>
/// Consulta el tiempo de inactividad GLOBAL del sistema (mouse + teclado en todo
/// Windows) usando <c>GetLastInputInfo</c>. Sirve para el auto-bloqueo por inactividad
/// real: mientras el operador use el equipo en cualquier ventana, no se bloquea.
/// </summary>
public static class SystemIdleService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>Tiempo transcurrido desde la ultima entrada de usuario (mouse/teclado).</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        // Environment.TickCount y dwTime son ms desde el arranque; la resta es robusta
        // ante el desbordamiento (ambos son uint con la misma referencia).
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
