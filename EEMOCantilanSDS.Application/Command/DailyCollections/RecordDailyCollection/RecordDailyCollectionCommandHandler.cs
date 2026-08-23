using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;

public class RecordDailyCollectionCommandHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    IPaymentRepository paymentRepository,
    IOrNumberRegistry orNumbers,
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    IFeeRateResolver feeRateResolver,
    ITenantContext tenantContext) : IRequestHandler<RecordDailyCollectionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordDailyCollectionCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        var isCollectorRequest = currentUser.Role == "Collector";
        if (isCollectorRequest)
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
            if (isCollectorRequest &&
                existing.CollectorId is { } recordedCollectorId &&
                collectorId is { } actingCollectorId &&
                recordedCollectorId != actingCollectorId)
            {
                return Result<bool>.Failure(
                    "This daily collection was already recorded by another collector. Refresh the record before making changes.",
                    ResultStatus.Conflict);
            }

            // Stamp the offline idempotency key on the UPDATE path too so a lost-ack retry is caught.
            if (request.ClientOperationId is { } existingOpId)
                existing.SetClientOperationId(existingOpId);

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
                    if (!alreadyOnThisRecord && !await orNumbers.IsAvailableAsync(orNumber, ct))
                        return Result<bool>.Failure("OR number already exists.", ResultStatus.Conflict);
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
            // Stamp the fee this stall is collected at, as of the collection date. Asked of the STALL, so an office that
            // prices the areas of its market apart is answered for the area this stall stands in, a stall in an area of
            // the market's own keeps the rate it was let at, and an office stating one rate for the whole market is
            // answered that rate exactly as before. A fee the office has never stated is refused rather than taken as
            // zero: the amount stamped here is what it reconciles against by hand.
            var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
            if (NpmDailyFee.ForStallOrNull(stall, rateSnapshot, request.CollectionDate) is not { } dailyFee)
                return Result<bool>.Failure(FeeRateMessages.NotStated(FeeRateKey.NpmDailyStall));

            var newCollection = DailyCollection.Create(
                stallId: request.StallId,
                collectionDate: request.CollectionDate,
                createdBy: recordedBy,
                dailyFee: dailyFee);

            if (request.ClientOperationId is { } clientOpId)
                newCollection.SetClientOperationId(clientOpId);

            if (request.IsAbsent)
            {
                newCollection.MarkAbsent(recordedBy);
            }
            else if (request.IsPaid)
            {
                if (!string.IsNullOrWhiteSpace(orNumber) && !await orNumbers.IsAvailableAsync(orNumber, ct))
                    return Result<bool>.Failure("OR number already exists.", ResultStatus.Conflict);

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
