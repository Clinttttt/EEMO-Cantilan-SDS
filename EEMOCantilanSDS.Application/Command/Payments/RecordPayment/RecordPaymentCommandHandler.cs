using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayment;

public class RecordPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<RecordPaymentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordPaymentCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall == null)
            return Result<bool>.NotFound();

        // NPM is a daily-collection facility (₱30/day). It must never carry a monthly PaymentRecord:
        // a monthly partial (e.g. ₱500) cannot be expressed as whole ₱30 days (16 × ₱30 = ₱480) and
        // ends up double-recorded against the daily ledger, so the history/calendar (daily-truth) and
        // the reports (monthly-record) disagree. NPM payments must go through daily collections.
        if (stall.Facility?.Code == FacilityCode.NPM)
            return Result<bool>.Failure(
                "New Public Market is collected daily — record payments via the daily collection calendar, not a monthly payment.",
                400);

        // Collectors may only record against a facility they are assigned to. Admins/heads
        // (any non-Collector role) record from the web and are not assignment-restricted.
        var isCollectorRequest = currentUser.Role == "Collector";
        if (isCollectorRequest)
        {
            if (currentUser.CollectorId is not { } actingCollectorId || stall.Facility is null)
                return Result<bool>.Forbidden();

            var actingCollector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (actingCollector is null ||
                !actingCollector.FacilityAssignments.Any(a => a.FacilityCode == stall.Facility.Code))
            {
                return Result<bool>.Forbidden();
            }
        }

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";
        var orNumber = request.ORNumber?.Trim();

        var existingPaymentDto = await paymentRepository.GetPaymentRecordAsync(
            request.StallId,
            request.Year,
            request.Month,
            ct);
        
        if (existingPaymentDto == null)
        {
            var newPayment = PaymentRecord.Create(
                request.StallId,
                request.Year,
                request.Month,
                stall.MonthlyRate,
                recordedBy
            );

            if (request.ClientOperationId is { } clientOpId)
                newPayment.SetClientOperationId(clientOpId);

            newPayment.UpdateStatus(request.Status, request.PartialAmount ?? 0m, request.Remarks, recordedBy, collectorId);
            if (newPayment.Status != PaymentStatus.Unpaid && !string.IsNullOrWhiteSpace(orNumber))
            {
                if (!await paymentRepository.IsMonthlyOrAvailableForStallAsync(orNumber, request.StallId, ct))
                    return Result<bool>.Failure("OR number already exists.", 409);
                newPayment.SetOrNumber(orNumber, recordedBy);
            }
            await paymentRepository.AddAsync(newPayment, ct);
        }
        else
        {
            var existingPayment = await paymentRepository.GetByIdAsync(existingPaymentDto.Id, ct);
            if (existingPayment == null)
                return Result<bool>.NotFound();

            // Stamp the offline idempotency key on the UPDATE path too (not just create), so a lost-ack
            // retry of the same queued op is caught by the sync pre-check instead of re-applying the write.
            if (request.ClientOperationId is { } clientOpId)
                existingPayment.SetClientOperationId(clientOpId);

            if (isCollectorRequest &&
                existingPayment.CollectorId is { } recordedCollectorId &&
                collectorId is { } actingCollectorId &&
                recordedCollectorId != actingCollectorId)
            {
                return Result<bool>.Failure(
                    "This payment was already recorded by another collector. Refresh the record before making changes.",
                    409);
            }
                
            existingPayment.UpdateStatus(request.Status, request.PartialAmount ?? 0m, request.Remarks, recordedBy, collectorId);
            if (existingPayment.Status != PaymentStatus.Unpaid && !string.IsNullOrWhiteSpace(orNumber))
            {
                // Allow re-saving the same OR already on THIS record (e.g. partial -> full re-record);
                // only reject when the OR is being introduced and already exists elsewhere.
                var alreadyOnThisRecord = string.Equals(existingPayment.ORNumber?.Trim(), orNumber, StringComparison.Ordinal);
                if (!alreadyOnThisRecord && !await paymentRepository.IsMonthlyOrAvailableForStallAsync(orNumber, request.StallId, ct))
                    return Result<bool>.Failure("OR number already exists.", 409);
                existingPayment.SetOrNumber(orNumber, recordedBy);
            }
            await paymentRepository.UpdateAsync(existingPayment, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            stall.Facility?.Code,
            request.Year,
            request.Month,
            ct);

        return Result<bool>.Success(true);
    }
}
