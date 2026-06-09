using Microsoft.AspNetCore.Mvc;
using QualityLock.Application.Interfaces;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(IEventService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Record(
        [FromBody] IEnumerable<StationEventRequest> requests,
        CancellationToken ct)
    {
        await service.RecordBatchAsync(requests, ct);
        return NoContent();
    }
}
