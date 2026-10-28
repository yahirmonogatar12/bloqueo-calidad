using Microsoft.AspNetCore.Mvc;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Controllers;

[ApiController]
[Route("api/mes")]
public class MesController(IVisionCheckService visionCheck) : ControllerBase
{
    /// <summary>¿Se puede teclear este codigo en la prueba? Solo barcodes EBR con Vision OK.</summary>
    [HttpGet("vision-check")]
    public async Task<ActionResult<VisionCheckResponse>> VisionCheck(
        [FromQuery] string code,
        [FromQuery] string? line,
        CancellationToken ct)
    {
        // La estacion sale del token (no del query); la linea la manda el cliente, igual que
        // en el bootstrap: el mismo codigo de estacion puede existir en varias lineas.
        var stationCode = User.FindFirst("station_code")?.Value ?? "";
        var result = await visionCheck.CheckAsync(code ?? "", stationCode, line?.Trim() ?? "", ct);
        return Ok(result);
    }

    // Una estacion manda a lo mucho 2 escaneos por aviso (el 1.o y el 2.o); el tope solo
    // evita que un cliente roto llene la tabla de golpe.
    private const int MaxScansPerReport = 20;

    /// <summary>La misma PCB se escaneo 2+ veces en una sesion del escaner COM: se registra.</summary>
    [HttpPost("repeated-scan")]
    public async Task<IActionResult> RepeatedScan([FromBody] RepeatedScanRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Code) || request.Scans is not { Count: > 0 and <= MaxScansPerReport })
            return BadRequest(new ApiErrorDto("INVALID_REQUEST", "Code y 1 a 20 escaneos son obligatorios."));

        var stationCode = User.FindFirst("station_code")?.Value ?? "";
        await visionCheck.RecordRepeatedScanAsync(stationCode, request with { Line = request.Line ?? "" }, ct);
        return NoContent();
    }
}
