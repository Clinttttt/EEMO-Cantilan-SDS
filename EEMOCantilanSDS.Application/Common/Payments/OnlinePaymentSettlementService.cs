using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <inheritdoc cref="IOnlinePaymentSettlementService"/>
public sealed class OnlinePaymentSettlementService(
    IPaymentRepository paymentRepository,
    IOnlinePaymentNotifier notifier,
    IUnitOfWork unitOfWork) : IOnlinePaymentSettlementService
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

        var record = await paymentRepository.GetByIdAsync(transaction.PaymentRecordId, cancellationToken);
        if (record is null)
            return Result<bool>.Failure("Linked payment record not found.", 500);

        // Cross-channel safety: between initiation and now, this period may already have been settled by
        // another channel (an offline collection, or a duplicate online transaction). The gateway has
        // still captured the payor's money, so we RECORD this transaction as Paid (above) for audit and
        // refund — but we must NOT overwrite the record's existing Paid status, OR number, or collector
        // attribution. Only clear the balance when the period is still outstanding (the normal case).
        if (record.Status != PaymentStatus.Paid)
        {
            record.MarkPaidOnline($"Paid online via {evt.Method ?? transaction.Provider} · ref {transaction.Reference}");
            await paymentRepository.UpdateAsync(record, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

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
}
