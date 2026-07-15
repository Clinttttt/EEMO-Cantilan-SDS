using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <inheritdoc cref="IOnlinePaymentSettlementService"/>
public sealed class OnlinePaymentSettlementService(
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    INpmMonthSettlementService npmMonthSettlementService,
    IUtilityBillRepository utilityBillRepository,
    IOnlinePaymentNotifier notifier,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IOnlinePaymentSettlementService
{
    public async Task<Result<bool>> SettleAsync(
        OnlinePaymentTransaction transaction,
        PaymentGatewayEvent evt,
        CancellationToken cancellationToken = default)
    {
        // Idempotency — already recorded as paid => no-op success (no duplicate save / notification).
        if (transaction.IsSettled)
            return Result<bool>.Success(true);

        // Amount integrity — never settle on a mismatch.
        if (Math.Round(evt.Amount, 2) != Math.Round(transaction.Amount, 2))
            return Result<bool>.Failure("Payment amount does not match the initiated amount.", 409);

        transaction.MarkPaid(evt.PaymentId, evt.Method, evt.PaidAt ?? DateTime.UtcNow, evt.RawPayload);

        // NPM daily-month: settle the month's still-unpaid days instead of a monthly record.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmDailyMonth)
            return await SettleNpmAsync(transaction, cancellationToken);

        // NPM utility bill: settle the month's electricity + water instead of a monthly record.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmUtilityBill)
            return await SettleNpmUtilityAsync(transaction, cancellationToken);

        // NPM fish day: mark that ONE day paid with the payor-declared kilos.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmFishDay)
            return await SettleNpmFishDayAsync(transaction, cancellationToken);

        var record = await paymentRepository.GetByIdAsync(transaction.PaymentRecordId!.Value, cancellationToken);
        if (record is null)
            return Result<bool>.Failure("Linked payment record not found.", 500);

        // Cross-channel safety: between initiation and now, this period may already have been settled by
        // another channel (an offline collection, or a duplicate online transaction). The gateway has
        // still captured the payor's money, so we RECORD this transaction as Paid (above) for audit and
        // refund — but we must NOT overwrite the record's existing Paid status, OR number, or collector
        // attribution. Only clear the balance when the period is still outstanding (the normal case).
        if (record.Status != PaymentStatus.Paid)
        {
            var note = $"Paid online via {evt.Method ?? transaction.Provider} · ref {transaction.Reference}";
            // Bill-changed-mid-checkout guard: the captured amount was frozen when the payor opened checkout.
            // If the balance has since grown (e.g. staff added a utility charge), the captured amount no
            // longer covers it — record it as PARTIAL for what was actually received rather than clearing
            // the full (larger) balance. Only for a still-Unpaid record; every other case is unchanged.
            if (record.Status == PaymentStatus.Unpaid && transaction.Amount < record.BalanceDue)
                record.MarkPartiallyPaidOnline(transaction.Amount, note + " · partial (balance increased after checkout)");
            else
                record.MarkPaidOnline(note);

            await paymentRepository.UpdateAsync(record, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            null,
            record.BillingYear,
            record.BillingMonth,
            cancellationToken);

        // Best-effort realtime alert for staff — must never affect payment processing.
        try
        {
            await notifier.NotifyPaymentReceivedAsync(
                new OnlinePaymentNotification(
                    transaction.Reference,
                    transaction.Amount,
                    record.PeriodKey,
                    transaction.Method,
                    transaction.PaidAt ?? DateTime.UtcNow,
                    record.StallId,
                    record.BillingYear,
                    record.BillingMonth),
                cancellationToken);
        }
        catch { /* notification is non-critical; the payment is already recorded */ }

        return Result<bool>.Success(true);
    }

    // NPM daily-month settlement: mark the month's still-unpaid, in-term, non-closed days Paid (blank OR;
    // staff encode the OR later). Cross-channel safe — any day already collected in person is skipped by
    // the shared service, so this never double-collects. If fewer days remain than were charged (some were
    // paid in person after checkout), the money is still recorded (transaction MarkPaid above) for audit /
    // refund, exactly as the monthly path records an already-settled period without overwriting it.
    private async Task<Result<bool>> SettleNpmAsync(OnlinePaymentTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.TargetStallId is not { } stallId
            || transaction.TargetYear is not { } year
            || transaction.TargetMonth is not { } month)
            return Result<bool>.Failure("NPM online payment is missing its target stall/month.", 500);

        var stall = await stallRepository.GetByIdAsync(stallId, cancellationToken);
        if (stall is null)
            return Result<bool>.Failure("Linked stall not found.", 500);

        await npmMonthSettlementService.SettleUnpaidDaysAsync(
            stall, year, month, collectorId: null, recordedBy: "Online", cancellationToken, maxAmount: transaction.Amount);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, year, month, cancellationToken);

        try
        {
            await notifier.NotifyPaymentReceivedAsync(
                new OnlinePaymentNotification(
                    transaction.Reference,
                    transaction.Amount,
                    $"{year:0000}-{month:00}",
                    transaction.Method,
                    transaction.PaidAt ?? DateTime.UtcNow,
                    stallId,
                    year,
                    month),
                cancellationToken);
        }
        catch { /* notification is non-critical; the payment is already recorded */ }

        return Result<bool>.Success(true);
    }

    // NPM utility settlement: mark the month's electricity + water Paid (blank OR; staff encode one OR
    // covering both later). Cross-channel safe — if the whole bill was already settled in person, leave it
    // untouched (money still recorded on the transaction for audit/refund); a still-unpaid side keeps its
    // blank OR, and any already-paid side keeps its OR (RecordPayment preserves it when no OR is passed).
    private async Task<Result<bool>> SettleNpmUtilityAsync(OnlinePaymentTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.TargetStallId is not { } stallId
            || transaction.TargetYear is not { } year
            || transaction.TargetMonth is not { } month)
            return Result<bool>.Failure("NPM online payment is missing its target stall/month.", 500);

        var bill = await utilityBillRepository.GetByStallAndMonthAsync(stallId, year, month, cancellationToken);
        if (bill is null)
            return Result<bool>.Failure("Linked utility bill not found.", 500);

        if (bill.Status != PaymentStatus.Paid)
        {
            var note = $"Paid online via {transaction.Method ?? transaction.Provider} · ref {transaction.Reference}";
            var captured = transaction.Amount;

            if (bill.BalanceDue <= captured)
            {
                // Captured amount covers the balance → mark both utilities Paid (normal case).
                bill.RecordPayment(
                    elecOrNumber: null, waterOrNumber: null, collectorId: null,
                    elecStatus: PaymentStatus.Paid, elecPartialAmount: null,
                    waterStatus: PaymentStatus.Paid, waterPartialAmount: null,
                    remarks: note, updatedBy: "Online");
            }
            else
            {
                // Balance grew beyond the captured amount (readings edited up while the checkout was open):
                // credit exactly what was received across electricity then water, leaving the grown remainder
                // as balance — never mark more paid than was captured (mirrors the monthly partial guard).
                var toElec = Math.Min(captured, bill.ElecBalanceDue);
                var toWater = Math.Min(captured - toElec, bill.WaterBalanceDue);
                var newElecPaid = bill.ElecAmountPaid + toElec;
                var newWaterPaid = bill.WaterAmountPaid + toWater;
                // RecordPayment.Normalize upgrades a Partial whose amount ≥ the charge to Paid.
                var elecStatus = newElecPaid <= 0m ? PaymentStatus.Unpaid : PaymentStatus.Partial;
                var waterStatus = newWaterPaid <= 0m ? PaymentStatus.Unpaid : PaymentStatus.Partial;
                bill.RecordPayment(
                    elecOrNumber: null, waterOrNumber: null, collectorId: null,
                    elecStatus: elecStatus, elecPartialAmount: newElecPaid,
                    waterStatus: waterStatus, waterPartialAmount: newWaterPaid,
                    remarks: note + " · partial (bill increased after checkout)", updatedBy: "Online");
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, year, month, cancellationToken);

        try
        {
            await notifier.NotifyPaymentReceivedAsync(
                new OnlinePaymentNotification(
                    transaction.Reference,
                    transaction.Amount,
                    $"{year:0000}-{month:00}",
                    transaction.Method,
                    transaction.PaidAt ?? DateTime.UtcNow,
                    stallId,
                    year,
                    month),
                cancellationToken);
        }
        catch { /* notification is non-critical; the payment is already recorded */ }

        return Result<bool>.Success(true);
    }

    // NPM fish-day settlement: mark THAT ONE day Paid with the payor-declared kilos (blank OR, no collector
    // — an online, payor-declared collection). Cross-channel safe — a day already collected in person
    // between checkout and settlement is left untouched by the shared service (money still recorded on the
    // transaction for audit/refund). Staff encode the OR for that day afterward.
    private async Task<Result<bool>> SettleNpmFishDayAsync(OnlinePaymentTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.TargetStallId is not { } stallId
            || transaction.TargetYear is not { } year
            || transaction.TargetMonth is not { } month
            || transaction.TargetDay is not { } day)
            return Result<bool>.Failure("NPM fish-day online payment is missing its target stall/day.", 500);

        var stall = await stallRepository.GetByIdAsync(stallId, cancellationToken);
        if (stall is null)
            return Result<bool>.Failure("Linked stall not found.", 500);

        var date = new DateOnly(year, month, day);
        await npmMonthSettlementService.SettleFishDayAsync(
            stall, date, transaction.DeclaredFishKilos ?? 0m, recordedBy: "Online", cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, year, month, cancellationToken);

        try
        {
            await notifier.NotifyPaymentReceivedAsync(
                new OnlinePaymentNotification(
                    transaction.Reference,
                    transaction.Amount,
                    $"{year:0000}-{month:00}",
                    transaction.Method,
                    transaction.PaidAt ?? DateTime.UtcNow,
                    stallId,
                    year,
                    month),
                cancellationToken);
        }
        catch { /* notification is non-critical; the payment is already recorded */ }

        return Result<bool>.Success(true);
    }
}
