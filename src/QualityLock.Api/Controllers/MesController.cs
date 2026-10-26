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
}
