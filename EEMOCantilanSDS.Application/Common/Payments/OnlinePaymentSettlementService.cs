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
    ITenantContext tenantContext, NpmRentMonthWorkflow? monthly = null) : IOnlinePaymentSettlementService
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
            return Result<bool>.Failure("Payment amount does not match the initiated amount.", ResultStatus.Conflict);

        // A pre-cutover checkout may still callback. Do not mark it settled or project it into day rows.
        if (monthly is not null && transaction.TargetKind is OnlinePaymentTargetKind.NpmDailyMonth
                or OnlinePaymentTargetKind.NpmWholeMonth or OnlinePaymentTargetKind.NpmFishDay or OnlinePaymentTargetKind.NpmFishDays
            && transaction.TargetStallId is { } target && transaction.TargetYear is { } year && transaction.TargetMonth is { } month
            && await monthly.IsMonthActivatedAsync(target, new(year, month, 1), cancellationToken))
            return await PreserveCaptureForReconciliationAsync(transaction, evt,
                "Captured gateway payment targets an activated NPM month. No legacy settlement was performed.", cancellationToken);

        PaymentRecord? monthlyRecord = null;
        if (transaction.TargetKind == OnlinePaymentTargetKind.MonthlyRecord)
        {
            monthlyRecord = transaction.PaymentRecordId is { } recordId
                ? await paymentRepository.GetByIdAsync(recordId, cancellationToken) : null;
            if (monthlyRecord is null || monthlyRecord.MunicipalityId != transaction.MunicipalityId)
                return Result<bool>.Failure("Linked monthly source not found in this municipality.", ResultStatus.Conflict);
            if (monthlyRecord.SettlementAuthorityState != SettlementAuthority.Legacy)
                return await PreserveCaptureForReconciliationAsync(transaction, evt,
                    "Gateway callback targets a transitioning or canonical monthly source. No Collection or legacy settlement was posted.", cancellationToken);
        }

        transaction.MarkPaid(evt.PaymentId, evt.Method, evt.PaidAt ?? DateTime.UtcNow, evt.RawPayload);

        // NPM daily-month: settle the month's still-unpaid days instead of a monthly record.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmDailyMonth)
            return await SettleNpmAsync(transaction, cancellationToken);

        // NPM whole month: settle the rest of the month, future days included, within the captured amount.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmWholeMonth)
            return await SettleNpmAsync(transaction, cancellationToken, wholeMonth: true);

        // NPM utility bill: settle the month's electricity + water instead of a monthly record.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmUtilityBill)
            return await SettleNpmUtilityAsync(transaction, cancellationToken);

        // NPM fish day: mark that ONE day paid with the payor-declared kilos.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmFishDay)
            return await SettleNpmFishDayAsync(transaction, cancellationToken);

        // NPM fish days: mark EACH of the days paid with the kilos declared for that day.
        if (transaction.TargetKind == OnlinePaymentTargetKind.NpmFishDays)
            return await SettleNpmFishDaysAsync(transaction, cancellationToken);

        var record = monthlyRecord;
        if (record is null)
            return Result<bool>.Failure("Linked payment record not found.", ResultStatus.Failed);

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
                    record.BillingMonth,
                    tenantContext.TenantCode),
                cancellationToken);
        }
        catch { /* notification is non-critical; the payment is already recorded */ }

        return Result<bool>.Success(true);
    }

    private async Task<Result<bool>> PreserveCaptureForReconciliationAsync(
        OnlinePaymentTransaction transaction, PaymentGatewayEvent evt, string reason, CancellationToken ct)
    {
        transaction.MarkPaid(evt.PaymentId, evt.Method, evt.PaidAt ?? DateTime.UtcNow, evt.RawPayload);
        transaction.MarkSettlementReconciliationRequired(reason);
        await unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Failure("RECONCILIATION_REQUIRED: " + reason, ResultStatus.Conflict);
    }

    // Cross-channel settlement leaves paid days intact; excess capture remains traceable for reconciliation/refund.
    private async Task<Result<bool>> SettleNpmAsync(OnlinePaymentTransaction transaction, CancellationToken cancellationToken, bool wholeMonth = false)
    {
        if (transaction.TargetStallId is not { } stallId
            || transaction.TargetYear is not { } year
            || transaction.TargetMonth is not { } month)
            return Result<bool>.Failure("NPM online payment is missing its target stall/month.", ResultStatus.Failed);

        var stall = await stallRepository.GetByIdAsync(stallId, cancellationToken);
        if (stall is null)
            return Result<bool>.Failure("Linked stall not found.", ResultStatus.Failed);

        if (wholeMonth)
            await npmMonthSettlementService.SettleWholeMonthAsync(
                stall, year, month, collectorId: null, recordedBy: "Online", cancellationToken, maxAmount: transaction.Amount);
        else
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
                    month,
                    tenantContext.TenantCode),
                cancellationToken);
        }
        catch { /* notification is non-critical; the payment is already recorded */ }

        return Result<bool>.Success(true);
    }

    // NPM fish-DAYS settlement: mark EACH day paid with the kilos declared for that day. Cross-channel safe for the
    // same reason the single-day path is — the shared service leaves a day already collected in person untouched, and
    // the money stays recorded on the transaction for audit or refund. Staff encode one OR across the days afterward.
    private async Task<Result<bool>> SettleNpmFishDaysAsync(OnlinePaymentTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.TargetStallId is not { } stallId
            || transaction.TargetYear is not { } year
            || transaction.TargetMonth is not { } month)
            return Result<bool>.Failure("NPM fish-days online payment is missing its target stall/month.", ResultStatus.Failed);

        var declarations = transaction.FishDays();
        if (declarations.Count == 0)
            return Result<bool>.Failure("NPM fish-days online payment is missing its days.", ResultStatus.Failed);

        var stall = await stallRepository.GetByIdAsync(stallId, cancellationToken);
        if (stall is null)
            return Result<bool>.Failure("Linked stall not found.", ResultStatus.Failed);

        var daysInMonth = DateTime.DaysInMonth(year, month);
        foreach (var declaration in declarations)
        {
            // A day the stored text cannot place in this month is skipped rather than allowed to throw: the money is
            // already captured, and the days that CAN be accounted for must still reach the office's register.
            if (declaration.Day < 1 || declaration.Day > daysInMonth) continue;

            await npmMonthSettlementService.SettleFishDayAsync(
                stall, new DateOnly(year, month, declaration.Day), declaration.Kilos, recordedBy: "Online", cancellationToken);
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
                    month,
                    tenantContext.TenantCode),
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
            return Result<bool>.Failure("NPM online payment is missing its target stall/month.", ResultStatus.Failed);

        var bill = await utilityBillRepository.GetByStallAndMonthAsync(stallId, year, month, cancellationToken);
        if (bill is null)
            return Result<bool>.Failure("Linked utility bill not found.", ResultStatus.Failed);

        // The legacy online checkout is one combined ECF/WCF settlement with an OR bridge. It cannot
        // safely settle either part once a scoped source cutover begins. The gateway has captured the
        // money, so retain the transaction as a reconciliation exception without mutating the bill.
        if (bill.ElectricitySettlementAuthorityState != SettlementAuthority.Legacy
            || bill.WaterSettlementAuthorityState != SettlementAuthority.Legacy)
        {
            transaction.MarkSettlementReconciliationRequired(
                "legacy combined NPM utility payment reached a source in Pending Cutover or Canonical authority");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode, FacilityCode.NPM, year, month, cancellationToken);
            return Result<bool>.Success(true);
        }

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
                    month,
                    tenantContext.TenantCode),
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
            return Result<bool>.Failure("NPM fish-day online payment is missing its target stall/day.", ResultStatus.Failed);

        var stall = await stallRepository.GetByIdAsync(stallId, cancellationToken);
        if (stall is null)
            return Result<bool>.Failure("Linked stall not found.", ResultStatus.Failed);

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
                    month,
                    tenantContext.TenantCode),
                cancellationToken);
        }
        catch { /* notification is non-critical; the payment is already recorded */ }

        return Result<bool>.Success(true);
    }
}
