using System.Diagnostics;

namespace QualityLock.Shared.Input;

/// <summary>
/// Distingue un escaneo (escaner QR/codigo de barras emulando teclado) de un tecleo
/// humano midiendo el tiempo entre pulsaciones. Un escaner inyecta los caracteres en
/// rafaga (pocos ms entre teclas); un humano tarda decenas/cientos de ms e irregular.
///
/// Uso: <see cref="RecordKey"/> en cada caracter, y al recibir Enter consultar
/// <see cref="LooksLikeScan"/>. <see cref="Reset"/> entre intentos.
/// Logica pura (sin UI) para poder probarse con xUnit.
/// </summary>
public class ScanSpeedDetector(int maxAvgKeyMs)
{
    private readonly Stopwatch _sw = new();
    private long _lastMs = -1;
    private int _keyCount;
    private double _totalGapMs;
    private double _maxGapMs;

    /// <summary>Promedio de ms entre teclas del ultimo intento (0 si no aplica).</summary>
    public double LastAvgGapMs => _keyCount > 1 ? _totalGapMs / (_keyCount - 1) : 0;

    /// <summary>Registra una pulsacion de caracter (no Enter/control).</summary>
    public void RecordKey()
    {
        if (!_sw.IsRunning) _sw.Restart();
        var now = _sw.ElapsedMilliseconds;

        if (_lastMs >= 0)
        {
            var gap = now - _lastMs;
            _totalGapMs += gap;
            if (gap > _maxGapMs) _maxGapMs = gap;
        }
        _lastMs = now;
        _keyCount++;
    }

    /// <summary>
    /// True si la entrada parece un escaneo: suficientes caracteres y un ritmo de tecleo
    /// rapido y uniforme. Un solo caracter no se puede medir, asi que se trata como
    /// "no escaneo" (obliga a la ruta con contrasena) por seguridad.
    /// </summary>
    public bool LooksLikeScan(int minChars = 2)
    {
        if (_keyCount < minChars) return false;

        // Promedio bajo el umbral configurado Y sin pausas largas (ninguna tecla con un
        // hueco > 3x el umbral, que delataria a un humano corrigiendo/pensando).
        return LastAvgGapMs <= maxAvgKeyMs
            && _maxGapMs <= maxAvgKeyMs * 3;
    }

    /// <summary>Reinicia el detector para el siguiente intento.</summary>
    public void Reset()
    {
        _sw.Reset();
        _lastMs = -1;
        _keyCount = 0;
        _totalGapMs = 0;
        _maxGapMs = 0;
    }
}
