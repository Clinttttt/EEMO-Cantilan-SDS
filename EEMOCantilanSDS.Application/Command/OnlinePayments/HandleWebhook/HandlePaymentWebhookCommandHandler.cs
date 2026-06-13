using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;

public class HandlePaymentWebhookCommandHandler(
    IPaymentGateway paymentGateway,
    IOnlinePaymentRepository onlinePaymentRepository,
    IPaymentRepository paymentRepository,
    IOnlinePaymentNotifier notifier,
    IUnitOfWork unitOfWork) : IRequestHandler<HandlePaymentWebhookCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(HandlePaymentWebhookCommand request, CancellationToken cancellationToken)
    {
        // 1) Authenticity — fail closed. No signature (or no configured secret) => reject.
        if (!paymentGateway.VerifyWebhookSignature(request.Payload, request.SignatureHeader ?? string.Empty))
            return Result<bool>.Unauthorized();

        // 2) Parse into a normalized event.
        var parsed = paymentGateway.ParseEvent(request.Payload);
        if (!parsed.IsSuccess || parsed.Value is null)
            return Result<bool>.Failure("Malformed webhook payload.", 400);

        var evt = parsed.Value;

        // Events we do not act on (or cannot correlate) are acknowledged so the provider stops retrying.
        if (evt.Type == PaymentGatewayEventType.Unknown || string.IsNullOrWhiteSpace(evt.GatewayReference))
            return Result<bool>.Success(true);

        var transaction = await onlinePaymentRepository.GetByGatewayReferenceAsync(evt.GatewayReference, cancellationToken);
        if (transaction is null)
            return Result<bool>.Success(true); // not one of ours — ack and ignore

        switch (evt.Type)
        {
            case PaymentGatewayEventType.Paid:
                // 3) Idempotency — if we already recorded this as paid, do nothing.
                if (transaction.IsSettled)
                    return Result<bool>.Success(true);

                // 4) Amount integrity — never settle on a mismatch.
                if (Math.Round(evt.Amount, 2) != Math.Round(transaction.Amount, 2))
                    return Result<bool>.Failure("Webhook amount does not match the initiated amount.", 409);

                transaction.MarkPaid(evt.PaymentId, evt.Method, evt.PaidAt ?? DateTime.UtcNow, evt.RawPayload);

                var record = await paymentRepository.GetByIdAsync(transaction.PaymentRecordId, cancellationToken);
                if (record is null)
                    return Result<bool>.Failure("Linked payment record not found.", 500);

                // Money received: clear the balance (delinquency recomputes as cleared) — OR stays
                // null until staff encode it; CollectorId stays null (online has no collector).
                record.MarkPaidOnline($"Paid online via {evt.Method ?? transaction.Provider} · ref {transaction.Reference}");
                await paymentRepository.UpdateAsync(record, cancellationToken);
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

            case PaymentGatewayEventType.Failed:
                transaction.MarkFailed(evt.RawPayload);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);

            case PaymentGatewayEventType.Expired:
                transaction.MarkExpired(evt.RawPayload);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);

            default:
                return Result<bool>.Success(true);
        }
    }
}
