using Microsoft.AspNetCore.Mvc;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Controllers;

[ApiController]
[Route("api/badges")]
public class BadgesController(IBadgeValidationService service) : ControllerBase
{
    [HttpPost("validate")]
    public async Task<ActionResult<BadgeValidationResponse>> Validate(
        [FromBody] BadgeValidationRequest request,
        CancellationToken ct)
    {
        var result = await service.ValidateAsync(request, ct);
        return Ok(result);
    }
}
