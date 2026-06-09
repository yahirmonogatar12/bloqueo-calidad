using Microsoft.AspNetCore.Mvc;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController(IAdminOverrideService service) : ControllerBase
{
    [HttpPost("override")]
    public async Task<ActionResult<AdminOverrideResponse>> Override(
        [FromBody] AdminOverrideRequest request,
        CancellationToken ct)
    {
        var result = await service.ProcessAsync(request, ct);
        return Ok(result);
    }
}
