using Microsoft.AspNetCore.Mvc;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController(ISessionService service) : ControllerBase
{
    [HttpPost("start")]
    public async Task<ActionResult<StartSessionResponse>> Start(
        [FromBody] StartSessionRequest request,
        CancellationToken ct)
    {
        var result = await service.StartAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("end")]
    public async Task<IActionResult> End(
        [FromBody] EndSessionRequest request,
        CancellationToken ct)
    {
        await service.EndAsync(request, ct);
        return NoContent();
    }
}
