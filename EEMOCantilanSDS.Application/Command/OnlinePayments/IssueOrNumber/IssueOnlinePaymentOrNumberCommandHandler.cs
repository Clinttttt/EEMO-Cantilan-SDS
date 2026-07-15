using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;

public class IssueOnlinePaymentOrNumberCommandHandler(
    IOnlinePaymentRepository onlinePaymentRepository,
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    IDailyCollectionRepository dailyCollectionRepository,
    IUtilityBillRepository utilityBillRepository,
    ICurrentUserService currentUser,
    IPayorRealtimeNotifier payorNotifier,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<IssueOnlinePaymentOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(IssueOnlinePaymentOrNumberCommand request, CancellationToken cancellationToken)
    {
        var transaction = await onlinePaymentRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction is null)
            return Result<bool>.NotFound();

        if (transaction.Status != OnlinePaymentStatus.Paid)
            return Result<bool>.Failure("Only an online payment awaiting OR can be receipted.", 409);

        // NPM daily-month: the OR is stamped across the month's settled days, not a monthly record.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmDailyMonth)
            return await IssueNpmOrAsync(transaction, request, cancellationToken);

        // NPM utility bill: one OR covers the month's electricity + water.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmUtilityBill)
            return await IssueNpmUtilityOrAsync(transaction, request, cancellationToken);

        var record = await paymentRepository.GetByIdAsync(transaction.PaymentRecordId!.Value, cancellationToken);
        if (record is null)
            return Result<bool>.Failure("Linked payment record not found.", 500);

        // A collector may only receipt an online payment for a facility they are assigned to; admins/heads are
        // unrestricted. Mirrors the collector guard on utility/collection payments (prevents a collector from
        // encoding ORs for facilities outside their assignment).
        if (string.Equals(currentUser.Role, "Collector", StringComparison.OrdinalIgnoreCase))
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<bool>.Forbidden();

            var stall = await stallRepository.GetByIdAsync(record.StallId, cancellationToken);
            var facilityCode = stall?.Facility?.Code;
            var collector = facilityCode is null ? null : await collectorRepository.GetByIdAsync(actingCollectorId, cancellationToken);
            if (collector is null || facilityCode is null
                || !collector.FacilityAssignments.Any(a => a.FacilityCode == facilityCode.Value))
                return Result<bool>.Forbidden();
        }

        var actor = currentUser.Username ?? "Admin";

        // Mirror the OR onto the ledger record (so it surfaces in normal reports) and complete the
        // online transaction. The OR is manual staff input — never auto-generated.
        record.SetOrNumber(request.ORNumber, actor);
        transaction.CompleteWithOr(request.ORNumber, actor);

        await paymentRepository.UpdateAsync(record, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            null,
            record.BillingYear,
            record.BillingMonth,
            cancellationToken);

        // Best-effort realtime alert to the paying payor: their provisional acknowledgment is now a
        // complete digital receipt. Must never affect the OR encoding above.
        try
        {
            await payorNotifier.NotifyOrIssuedAsync(
                transaction.PayorUserId,
                new PayorOrIssuedNotification(
                    transaction.Reference,
                    request.ORNumber,
                    transaction.Amount,
                    record.PeriodKey,
                    record.StallId),
                cancellationToken);
        }
        catch { /* notification is non-critical; the OR is already recorded */ }

        return Result<bool>.Success(true);
    }

    // NPM daily-month: stamp the staff OR across the month's paid days that still lack one (the days this
    // payment settled — "one receipt covers the whole month", mirroring SettleNpmMonth). The OR is manual
    // staff input, never auto-generated, and checked stall-aware for uniqueness.
    private async Task<Result<bool>> IssueNpmOrAsync(
        Domain.Entities.Payments.OnlinePaymentTransaction transaction,
        IssueOnlinePaymentOrNumberCommand request,
        CancellationToken cancellationToken)
    {
        if (transaction.TargetStallId is not { } stallId
            || transaction.TargetYear is not { } year
            || transaction.TargetMonth is not { } month)
            return Result<bool>.Failure("NPM online payment is missing its target stall/month.", 500);

        // Collector facility guard (same as the monthly path): a collector may only receipt for a facility
        // they're assigned to; admins/heads are unrestricted.
        if (string.Equals(currentUser.Role, "Collector", StringComparison.OrdinalIgnoreCase))
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<bool>.Forbidden();

            var stall = await stallRepository.GetByIdAsync(stallId, cancellationToken);
            var facilityCode = stall?.Facility?.Code;
            var collector = facilityCode is null ? null : await collectorRepository.GetByIdAsync(actingCollectorId, cancellationToken);
            if (collector is null || facilityCode is null
                || !collector.FacilityAssignments.Any(a => a.FacilityCode == facilityCode.Value))
                return Result<bool>.Forbidden();
        }

        var actor = currentUser.Username ?? "Admin";
        var orNumber = request.ORNumber.Trim();

        // Stall-aware OR uniqueness — a single receipt may cover many days of the SAME stall.
        if (!await paymentRepository.IsDailyCollectionOrAvailableForStallAsync(orNumber, stallId, cancellationToken))
            return Result<bool>.Failure("OR number already exists.", 409);

        var days = (await dailyCollectionRepository.GetByStallAndMonthAsync(stallId, year, month, cancellationToken))
            .Where(dc => dc.IsPaid && string.IsNullOrWhiteSpace(dc.ORNumber))
            .ToList();
        foreach (var dc in days)
            dc.SetOrNumber(orNumber, actor);

        transaction.CompleteWithOr(orNumber, actor);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, year, month, cancellationToken);

        try
        {
            await payorNotifier.NotifyOrIssuedAsync(
                transaction.PayorUserId,
                new PayorOrIssuedNotification(
                    transaction.Reference,
                    orNumber,
                    transaction.Amount,
                    $"{year:0000}-{month:00}",
                    stallId),
                cancellationToken);
        }
        catch { /* notification is non-critical; the OR is already recorded */ }

        return Result<bool>.Success(true);
    }

    // NPM utility bill: one staff OR covers the month's electricity + water (both were marked Paid at
    // settlement). Only stamps the OR on a side that doesn't already have one, so a utility that was
    // receipted in person keeps its own OR. Stall-aware/utility uniqueness is enforced.
    private async Task<Result<bool>> IssueNpmUtilityOrAsync(
        Domain.Entities.Payments.OnlinePaymentTransaction transaction,
        IssueOnlinePaymentOrNumberCommand request,
        CancellationToken cancellationToken)
    {
        if (transaction.TargetStallId is not { } stallId
            || transaction.TargetYear is not { } year
            || transaction.TargetMonth is not { } month)
            return Result<bool>.Failure("NPM online payment is missing its target stall/month.", 500);

        if (string.Equals(currentUser.Role, "Collector", StringComparison.OrdinalIgnoreCase))
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<bool>.Forbidden();

            var stall = await stallRepository.GetByIdAsync(stallId, cancellationToken);
            var facilityCode = stall?.Facility?.Code;
            var collector = facilityCode is null ? null : await collectorRepository.GetByIdAsync(actingCollectorId, cancellationToken);
            if (collector is null || facilityCode is null
                || !collector.FacilityAssignments.Any(a => a.FacilityCode == facilityCode.Value))
                return Result<bool>.Forbidden();
        }

        var actor = currentUser.Username ?? "Admin";
        var orNumber = request.ORNumber.Trim();

        var bill = await utilityBillRepository.GetByStallAndMonthAsync(stallId, year, month, cancellationToken);
        if (bill is null)
            return Result<bool>.Failure("Linked utility bill not found.", 500);

        if (!await utilityBillRepository.IsORNumberUniqueAsync(orNumber, bill.Id, cancellationToken))
            return Result<bool>.Failure("OR number already exists.", 409);

        // Preserve an already-receipted side's OR (paid in person); stamp the new OR only on blank sides.
        var elecOr = string.IsNullOrWhiteSpace(bill.ElecORNumber) ? orNumber : null;
        var waterOr = string.IsNullOrWhiteSpace(bill.WaterORNumber) ? orNumber : null;
        bill.RecordPayment(
            elecOrNumber: elecOr, waterOrNumber: waterOr, collectorId: currentUser.CollectorId,
            elecStatus: PaymentStatus.Paid, elecPartialAmount: null,
            waterStatus: PaymentStatus.Paid, waterPartialAmount: null,
            updatedBy: actor);

        transaction.CompleteWithOr(orNumber, actor);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, year, month, cancellationToken);

        try
        {
            await payorNotifier.NotifyOrIssuedAsync(
                transaction.PayorUserId,
                new PayorOrIssuedNotification(
                    transaction.Reference,
                    orNumber,
                    transaction.Amount,
                    $"{year:0000}-{month:00}",
                    stallId),
                cancellationToken);
        }
        catch { /* notification is non-critical; the OR is already recorded */ }

        return Result<bool>.Success(true);
    }
}
