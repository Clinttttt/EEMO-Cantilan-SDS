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
    ITenantContext tenantContext,
    Common.Revenue.NpmDailyCanonicalPoster? canonical = null) : IRequestHandler<RecordDailyCollectionCommand, Result<bool>>
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

        // IA-051/IA-062: from the business date the Head turns the NPM daily switch on, a NEW daily stall-fee payment is
        // canonical money - one Collection with an SRC - whoever records it. The market rules below are unchanged; only where
        // the money lives changes, and no typed receipt serial is carried on that path.
        var paymentDate = PhilippineTime.Today;
        var canonicalMoney = canonical is not null && stall.Facility?.Code == FacilityCode.NPM
            && await canonical.IsCanonicalAsync(paymentDate, ct);
        var operationId = request.ClientOperationId ?? Guid.NewGuid();
        var intent = canonicalMoney
            ? canonical!.NormalizeIntent("NpmDailyRecord", request.StallId, [request.CollectionDate], paymentDate,
                $"paid={request.IsPaid};absent={request.IsAbsent}")
            : null;
        if (canonicalMoney && request.ClientOperationId is not null && request.IsPaid && !request.IsAbsent
            && await canonical!.FindPriorAsync(operationId, intent!, ct) is { } prior)
            return prior.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(prior.Error!, ResultStatus.Conflict);
        DailyCollection? newlyPaid = null;
        var orNumber = canonicalMoney ? null : request.ORNumber?.Trim();

        if (request.MeatKilos is < 0m)
            return Result<bool>.Failure("Meat kilos cannot be negative.", ResultStatus.Invalid);

        if (request.MeatKilos.HasValue
            && (request.IsAbsent || !request.IsPaid
                || stall.Facility?.Code != FacilityCode.NPM
                || stall.Section != MarketSection.MeatSection))
            return Result<bool>.Failure("Meat kilos can only be recorded on a paid NPM Meat-section collection.", ResultStatus.Invalid);

        var existing = await dailyCollectionRepository.GetByStallAndDateAsync(request.StallId, request.CollectionDate, ct);
        // A legacy client omitting the newly-added optional weight field must not erase already captured
        // Meat source evidence on an update. Clearing/voiding still follows the existing unpaid/absent path.
        var meatKilosToSave = request.MeatKilos;
        var meatRateToSave = (decimal?)null;
        var meatRateEffectiveDateToSave = (DateOnly?)null;
        FeeRateEntry? meatRate = null;
        FeeRateSnapshot? rateSnapshot = null;
        // Fish weighing evidence: resolved once at collection time and never re-priced on a later re-mark.
        var fishKilosPaid = request.IsPaid && !request.IsAbsent && request.FishKilos is > 0m;
        if (request.MeatKilos.HasValue || existing is null || fishKilosPaid)
        {
            rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
            if (request.MeatKilos.HasValue)
            {
                meatRate = rateSnapshot.ResolveEntryOrNull(FeeRateKey.NpmMeatPerKilo, request.CollectionDate);
                if (meatRate is null || meatRate.Value.Amount <= 0m)
                    return Result<bool>.Failure(FeeRateMessages.NotStated(FeeRateKey.NpmMeatPerKilo), ResultStatus.Conflict);
                meatRateToSave = meatRate.Value.Amount;
                meatRateEffectiveDateToSave = meatRate.Value.EffectiveDate;
            }
        }
        // Freeze the rate in force on the collection date. A re-mark with unchanged kilos keeps the evidence it already
        // has (a later rate must not re-price an earlier weighing); an office that has stated no Fish rate freezes
        // nothing, so the row stays unresolved rather than being given an invented amount.
        var fishRateToSave = (decimal?)null;
        var fishRateEffectiveDateToSave = (DateOnly?)null;
        if (fishKilosPaid)
        {
            if (existing is { FishFeeRatePerKilo: not null } && existing.FishKilos == request.FishKilos)
            {
                fishRateToSave = existing.FishFeeRatePerKilo;
                fishRateEffectiveDateToSave = existing.FishFeeRateEffectiveDate;
            }
            else if (rateSnapshot?.ResolveEntryOrNull(FeeRateKey.NpmFishPerKilo, request.CollectionDate) is { Amount: > 0m } fishRate)
            {
                fishRateToSave = fishRate.Amount;
                fishRateEffectiveDateToSave = fishRate.EffectiveDate;
            }
        }

        if (existing is not null && request.IsPaid && !request.MeatKilos.HasValue && existing.MeatKilos.HasValue)
        {
            meatKilosToSave = existing.MeatKilos;
            meatRateToSave = existing.MeatFeeRatePerKilo;
            meatRateEffectiveDateToSave = existing.MeatFeeRateEffectiveDate;
        }

        if (existing is not null)
        {
            if (existing.CanonicalAdjustmentCollectionId is not null)
                return Result<bool>.Failure("This installment carries a posted month-end adjustment. Correct the collection before editing it.", ResultStatus.Conflict);
            if (isCollectorRequest &&
                existing.CollectorId is { } recordedCollectorId &&
                collectorId is { } actingCollectorId &&
                recordedCollectorId != actingCollectorId)
            {
                return Result<bool>.Failure(
                    "This daily collection was already recorded by another collector. Refresh the record before making changes.",
                    ResultStatus.Conflict);
            }

            // A day paid by a canonical Collection is append-only financial history: it is not un-marked or excused here.
            // Its correction is the void of that Collection, which projects the day unpaid.
            if ((existing.SettlementAuthorityState == SettlementAuthority.Canonical || existing.CanonicalAdjustmentCollectionId is not null)
                && (existing.IsPaid || existing.FishKilos is > 0m || existing.MeatFeeAmount > 0m)
                && (request.IsAbsent || !request.IsPaid))
                return Result<bool>.Failure(
                    "This day was paid by a posted collection. Void that collection to correct it; the day cannot be un-marked here.",
                    ResultStatus.Conflict);

            if (existing.SettlementAuthorityState == SettlementAuthority.Canonical && !existing.IsPaid
                && ((request.FishKilos.HasValue && request.FishKilos != existing.FishKilos)
                    || (request.MeatKilos.HasValue && request.MeatKilos != existing.MeatKilos)))
                return Result<bool>.Failure("Existing weighing evidence cannot be replaced by a rent payment.", ResultStatus.Conflict);

            // Stamp the offline idempotency key on the UPDATE path too so a lost-ack retry is caught.
            if (request.ClientOperationId is { } existingOpId)
                existing.SetClientOperationId(existingOpId);

            if (!existing.IsPaid && request.IsPaid && !request.IsAbsent) newlyPaid = existing;

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
                    updatedBy: recordedBy,
                    meatKilos: meatKilosToSave,
                    meatFeeRatePerKilo: meatRateToSave,
                    meatFeeRateEffectiveDate: meatRateEffectiveDateToSave,
                    fishFeeRatePerKilo: fishRateToSave,
                    fishFeeRateEffectiveDate: fishRateEffectiveDateToSave);
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
            rateSnapshot ??= await feeRateResolver.GetSnapshotAsync(ct);
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
                    updatedBy: recordedBy,
                    meatKilos: meatKilosToSave,
                    meatFeeRatePerKilo: meatRateToSave,
                    meatFeeRateEffectiveDate: meatRateEffectiveDateToSave,
                    fishFeeRatePerKilo: fishRateToSave,
                    fishFeeRateEffectiveDate: fishRateEffectiveDateToSave);
            }

            await dailyCollectionRepository.AddAsync(newCollection, ct);
            if (request.IsPaid && !request.IsAbsent) newlyPaid = newCollection;
        }

        if (canonicalMoney && newlyPaid is not null)
        {
            // The day's stall fee is posted as one Collection (the SRC is its identity). Weighing recorded with the day stays
            // on the day row under Weight and Measure and is not part of this Collection.
            var posted = await canonical!.PostAsync(stall, [new(newlyPaid, newlyPaid.DailyFee)], operationId, intent!, paymentDate, ct);
            if (!posted.IsSuccess)
                return Result<bool>.Failure(posted.Error ?? "The collection could not be posted.", posted.Status);
        }
        else
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
