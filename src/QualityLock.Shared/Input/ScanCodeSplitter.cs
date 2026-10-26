using System.Text;

namespace QualityLock.Shared.Input;

/// <summary>
/// Separa en codigos el flujo de caracteres de un escaner por puerto serie. El puerto
/// entrega trozos arbitrarios (un codigo puede llegar partido en dos lecturas), asi que
/// lo incompleto se acumula en <c>buffer</c> hasta que llega CR o LF.
/// Logica pura (sin UI ni puerto) para poder probarse con xUnit.
/// </summary>
public static class ScanCodeSplitter
{
    /// <summary>Devuelve los codigos terminados en CR/LF (Trim, sin vacios).</summary>
    public static List<string> Take(StringBuilder buffer, string chunk)
    {
        var codes = new List<string>();
        foreach (var c in chunk)
        {
            if (c != '\r' && c != '\n')
            {
                buffer.Append(c);
                continue;
            }

            var code = buffer.ToString().Trim();
            buffer.Clear();
            if (code.Length > 0)
                codes.Add(code);
        }
        return codes;
    }
}
