using System.Globalization;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Application.Services;

public class VisionCheckService(
    IVisionHistoryRepository visionRepo,
    IVisionRejectionRepository rejectionRepo) : IVisionCheckService
{
    // El motivo se ve en el aviso rojo de la estacion: solo las primeras fallas.
    private const int MaxDefectsInReason = 2;

    public async Task<VisionCheckResponse> CheckAsync(
        string code, string stationCode = "", string line = "", CancellationToken ct = default)
    {
        var (result, type) = await EvaluateAsync(code.Trim(), ct);
        if (!result.Allowed)
            await SaveRejectionAsync(result, type, stationCode, line, ct);
        return result;
    }

    private async Task<(VisionCheckResponse Result, string Type)> EvaluateAsync(string code, CancellationToken ct)
    {
        // Solo barcodes de tarjeta (EBR...). Etiquetas QR u otros codigos no entran a la prueba.
        if (!code.StartsWith("EBR", StringComparison.OrdinalIgnoreCase))
            return (new VisionCheckResponse(code, false, "No es un barcode EBR."), "NO_EBR");

        var last = await visionRepo.GetLastAsync(code, ct);
        if (last is null)
            return (new VisionCheckResponse(code, false, "Sin registro en Visión."), "SIN_HISTORIAL");

        var ok = string.Equals(last.Result, "OK", StringComparison.OrdinalIgnoreCase);
        var reason = ok ? "Visión OK." : $"Última inspección de Visión: {last.Result}.";
        if (!ok)
            reason += await DescribeDefectsAsync(last.SourceUid, ct);

        return (new VisionCheckResponse(code, ok, reason, last.Result, last.InspectedAt, last.Machine), "NG");
    }

    private async Task SaveRejectionAsync(
        VisionCheckResponse result, string type, string stationCode, string line, CancellationToken ct)
    {
        try
        {
            await rejectionRepo.AddAsync(new VisionRejection(
                DateTime.UtcNow, stationCode, line, result.Code, type, result.Reason,
                result.Machine, result.InspectedAt), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // El registro es informativo: si falla (o la tabla aun no existe) el rechazo sigue igual.
        }
    }

    /// <summary>" Falla: Conector 2: Maker-Direction-Percent 84.26 (+1 más)." o "" sin detalle.</summary>
    private async Task<string> DescribeDefectsAsync(string sourceUid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceUid))
            return "";

        IReadOnlyList<VisionDefect>? defects;
        try
        {
            defects = await visionRepo.GetDefectsAsync(sourceUid, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return ""; // el detalle es informativo: sin el, el rechazo sigue igual
        }

        if (defects is null || defects.Count == 0)
            return "";

        var shown = string.Join("; ", defects.Take(MaxDefectsInReason).Select(Describe));
        var more = defects.Count > MaxDefectsInReason ? $" (+{defects.Count - MaxDefectsInReason} más)" : "";
        return $" Falla: {shown}{more}.";
    }

    private static string Describe(VisionDefect d)
    {
        var where = string.Join(" ", new[] { d.TestName, d.Position }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var value = d.ValueNum is double n
            ? n.ToString("0.##", CultureInfo.InvariantCulture) + (d.ValueText.Contains('%') ? "%" : "")
            : d.ValueText.Length > 40 ? d.ValueText[..40] + "…" : d.ValueText;
        var what = string.Join(" ", new[] { d.SubTest, value }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return what.Length > 0 ? $"{where}: {what}" : where;
    }
}
