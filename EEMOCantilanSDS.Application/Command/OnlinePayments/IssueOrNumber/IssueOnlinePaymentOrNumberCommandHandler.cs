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

        var record = await paymentRepository.GetByIdAsync(transaction.PaymentRecordId, cancellationToken);
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
}
