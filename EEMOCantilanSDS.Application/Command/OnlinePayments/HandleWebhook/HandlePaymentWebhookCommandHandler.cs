using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;

public class HandlePaymentWebhookCommandHandler(
    IPaymentGateway paymentGateway,
    IOnlinePaymentRepository onlinePaymentRepository,
    IOnlinePaymentSettlementService settlementService,
    IUnitOfWork unitOfWork,
    IMunicipalityRepository municipalityRepository,
    IPayMongoCredentialResolver credentialResolver,
    IRequestTenantScope tenantScope) : IRequestHandler<HandlePaymentWebhookCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(HandlePaymentWebhookCommand request, CancellationToken cancellationToken)
    {
        // 1) Authenticity — fail closed. For a per-LGU webhook URL, pin that LGU FIRST and verify against
        // ITS webhook secret; the default webhook (no tenant code) verifies against the global secret so
        // Cantilan's path is byte-for-byte unchanged.
        bool verified;
        if (!string.IsNullOrWhiteSpace(request.TenantCode))
        {
            var lgu = await municipalityRepository.GetByIdentifierAsync(request.TenantCode!, cancellationToken);
            if (lgu is null)
                return Result<bool>.Unauthorized();

            tenantScope.Use(lgu.Id, lgu.TenantCode);
            var credentials = await credentialResolver.ResolveAsync(cancellationToken);
            verified = paymentGateway.VerifyWebhookSignature(
                request.Payload, request.SignatureHeader ?? string.Empty, credentials.WebhookSecret ?? string.Empty);
        }
        else
        {
            verified = paymentGateway.VerifyWebhookSignature(request.Payload, request.SignatureHeader ?? string.Empty);
        }

        if (!verified)
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

        // The webhook is anonymous, so this request would otherwise resolve to the DEFAULT tenant (Cantilan).
        // Pin it to the transaction's own LGU so the settlement's payment-record lookup, write-stamping, and
        // cache invalidation all run under the correct municipality (multi-LGU correctness). No-op for the
        // default tenant, so Cantilan is unchanged.
        var municipality = await municipalityRepository.GetByIdAsync(transaction.MunicipalityId, cancellationToken);
        if (municipality is not null)
            tenantScope.Use(municipality.Id, municipality.TenantCode);

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
