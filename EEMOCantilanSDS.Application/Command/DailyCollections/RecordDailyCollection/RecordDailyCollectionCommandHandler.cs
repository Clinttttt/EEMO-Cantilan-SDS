using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;

public class RecordDailyCollectionCommandHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<RecordDailyCollectionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordDailyCollectionCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId || stall.Facility is null)
                return Result<bool>.Forbidden();

            var collector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (collector is null ||
                !collector.FacilityAssignments.Any(a => a.FacilityCode == stall.Facility.Code))
            {
                return Result<bool>.Forbidden();
            }
        }

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "System";
        var orNumber = request.ORNumber?.Trim();

        var existing = await dailyCollectionRepository.GetByStallAndDateAsync(request.StallId, request.CollectionDate, ct);

        if (existing is not null)
        {
            if (request.IsAbsent)
            {
                existing.MarkAbsent(recordedBy);
            }
            else if (request.IsPaid)
            {
                if (!string.IsNullOrWhiteSpace(orNumber))
                {
                    // Permit re-marking with the OR already on this day; reject a new OR used elsewhere.
                    var alreadyOnThisRecord = string.Equals(existing.ORNumber?.Trim(), orNumber, StringComparison.Ordinal);
                    if (!alreadyOnThisRecord && !await paymentRepository.IsORNumberUniqueAsync(orNumber, ct))
                        return Result<bool>.Failure("OR number already exists.", 409);
                }

                existing.MarkPaid(
                    orNumber: orNumber ?? string.Empty,
                    collectorId: collectorId,
                    fishKilos: request.FishKilos,
                    updatedBy: recordedBy);
            }
            else
            {
                existing.MarkUnpaid(recordedBy);
            }
        }
        else
        {
            var newCollection = DailyCollection.Create(
                stallId: request.StallId,
                collectionDate: request.CollectionDate,
                createdBy: recordedBy);

            if (request.ClientOperationId is { } clientOpId)
                newCollection.SetClientOperationId(clientOpId);

            if (request.IsAbsent)
            {
                newCollection.MarkAbsent(recordedBy);
            }
            else if (request.IsPaid)
            {
                if (!string.IsNullOrWhiteSpace(orNumber) && !await paymentRepository.IsORNumberUniqueAsync(orNumber, ct))
                    return Result<bool>.Failure("OR number already exists.", 409);

                newCollection.MarkPaid(
                    orNumber: orNumber ?? string.Empty,
                    collectorId: collectorId,
                    fishKilos: request.FishKilos,
                    updatedBy: recordedBy);
            }

            await dailyCollectionRepository.AddAsync(newCollection, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            stall.Facility?.Code,
            request.CollectionDate.Year,
            request.CollectionDate.Month,
            ct);

        return Result<bool>.Success(true);
    }
}
