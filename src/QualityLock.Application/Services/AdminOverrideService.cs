using System.Text.Json;
using QualityLock.Application.Exceptions;
using QualityLock.Application.Interfaces;
using QualityLock.Domain.Entities;
using QualityLock.Shared.DTOs;
using QualityLock.Shared.Enums;

namespace QualityLock.Application.Services;

public class AdminOverrideService(
    IStationRepository stationRepo,
    IOperatorRepository operatorRepo,
    IAdminOverrideRepository overrideRepo,
    IEventRepository eventRepo) : IAdminOverrideService
{
    public async Task<AdminOverrideResponse> ProcessAsync(AdminOverrideRequest request, CancellationToken ct = default)
    {
        var station = await stationRepo.GetByCodeAndLineAsync(request.StationCode, request.Line, ct)
            ?? throw new NotFoundException($"Station '{request.StationCode}' not found.");

        var adminOp = await operatorRepo.GetByBadgeCodeAsync(request.AdminBadgeCode, ct)
            ?? throw new NotFoundException("Admin operator not found.");

        if (!adminOp.IsAdmin || !adminOp.IsActive)
            throw new UnauthorizedException("Operator is not an active admin.");

        int? targetOperatorId = null;
        if (request.TargetBadgeCode is not null)
        {
            var target = await operatorRepo.GetByBadgeCodeAsync(request.TargetBadgeCode, ct);
            targetOperatorId = target?.Id;
        }

        var overrideRecord = new AdminOverride
        {
            Id = Guid.NewGuid(),
            StationId = station.Id,
            AdminOperatorId = adminOp.Id,
            TargetOperatorId = targetOperatorId,
            Reason = request.Reason,
            Comments = request.Comments,
            Approved = true,
            CreatedAtUtc = request.ClientUtc
        };

        var overrideId = await overrideRepo.CreateAsync(overrideRecord, ct);

        await eventRepo.InsertAsync(new StationEvent
        {
            Id = Guid.NewGuid(),
            StationId = station.Id,
            OperatorId = adminOp.Id,
            EventType = StationEventType.AdminOverrideApproved,
            EventAtUtc = request.ClientUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                overrideId,
                reason = request.Reason.ToString(),
                comments = request.Comments
            }),
            Source = "API",
            CorrelationId = Guid.NewGuid().ToString()
        }, ct);

        return new AdminOverrideResponse(overrideId, true, "Override approved and recorded.");
    }
}
