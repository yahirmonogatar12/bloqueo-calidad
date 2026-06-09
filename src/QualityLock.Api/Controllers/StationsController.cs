using Microsoft.AspNetCore.Mvc;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Controllers;

[ApiController]
[Route("api/stations")]
public class StationsController(
    IStationBootstrapService bootstrapService,
    IStationRegistrationService registrationService) : ControllerBase
{
    [HttpGet("{stationCode}/bootstrap")]
    public async Task<ActionResult<StationBootstrapResponse>> Bootstrap(
        string stationCode, CancellationToken ct)
    {
        var result = await bootstrapService.GetBootstrapAsync(stationCode, ct);
        return Ok(result);
    }

    [HttpPut("{stationCode}")]
    public async Task<ActionResult<RegisterStationResponse>> Register(
        string stationCode,
        [FromBody] RegisterStationRequest request,
        CancellationToken ct)
    {
        if (!string.Equals(stationCode, request.StationCode, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ApiErrorDto("BAD_REQUEST", "StationCode in URL and body do not match."));

        var result = await registrationService.RegisterAsync(request, ct);
        return result.Created ? StatusCode(201, result) : Ok(result);
    }
}
