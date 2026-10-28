using QualityLock.Shared.DTOs;

namespace QualityLock.Shared.Input;

/// <summary>
/// Escaneos de cada PCB dentro de una sesion del escaner COM (de que se conecta el puerto a
/// que se desconecta). Dice que escaneos registrar como repetidos: en el 2.o, el 1.o y el
/// 2.o (cada uno con su hora); del 3.o en adelante, solo el nuevo.
/// Thread-safe. Logica pura (sin UI ni puerto) para poder probarse con xUnit.
/// </summary>
public sealed class ScanRepeatTracker
{
    // ponytail: una sesion puede durar dias con el escaner conectado; al pasar este numero
    // de PCBs distintas se empieza de cero (se pierde la deteccion solo en ese instante).
    private const int MaxCodes = 20_000;

    private readonly object _lock = new();
    private readonly Dictionary<string, List<DateTime>> _scans = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registra un escaneo. Devuelve los escaneos a registrar como repetidos (vacio si es el 1.o).</summary>
    public IReadOnlyList<RepeatedScanItem> Add(string code, DateTime atUtc)
    {
        lock (_lock)
        {
            if (!_scans.TryGetValue(code, out var times))
            {
                if (_scans.Count >= MaxCodes) _scans.Clear();
                _scans[code] = [atUtc];
                return [];
            }

            times.Add(atUtc);
            if (times.Count == 2)
                return [new RepeatedScanItem(1, times[0]), new RepeatedScanItem(2, times[1])];
            return [new RepeatedScanItem(times.Count, atUtc)];
        }
    }

    /// <summary>Sesion nueva (el puerto se acaba de conectar).</summary>
    public void Reset()
    {
        lock (_lock) _scans.Clear();
    }
}
