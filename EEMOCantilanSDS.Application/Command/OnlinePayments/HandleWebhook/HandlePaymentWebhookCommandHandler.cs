using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;

public class HandlePaymentWebhookCommandHandler(
    IPaymentGateway paymentGateway,
    IOnlinePaymentRepository onlinePaymentRepository,
    IOnlinePaymentSettlementService settlementService,
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
                // Single, idempotent settle path shared with the confirmation/reconciliation fallback.
                return await settlementService.SettleAsync(transaction, evt, cancellationToken);

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
