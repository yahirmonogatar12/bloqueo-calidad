using Microsoft.AspNetCore.Mvc;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Controllers;

[ApiController]
[Route("api/heartbeats")]
public class HeartbeatsController(IHeartbeatService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Record(
        [FromBody] HeartbeatRequest request,
        CancellationToken ct)
    {
        await service.RecordAsync(request, ct);
        return NoContent();
    }
}
