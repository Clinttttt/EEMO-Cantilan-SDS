using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// Settles or closes one open checkout from what the gateway says. PayMongo does not resend a missed webhook, and a checkout stays
/// open after its QR code lapses, so without this a lost webhook or an abandoned checkout stays Pending for good.
/// </summary>
/// <remarks>
/// A failed attempt does not end a checkout: the payor can retry in the same session, so a checkout only ends when it is closed here
/// or by a replacement in Initiate. A database conflict is left to the caller: another writer won, and the next pass reads its result.
/// </remarks>
public sealed class OnlinePaymentReconciler(
    IOnlinePaymentRepository transactions,
    IPaymentGateway gateway,
    IPayMongoCheckoutExpiryService checkoutExpiry,
    IOnlinePaymentSettlementService settlement,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>Left alone before this, because the webhook usually arrives first and the payor may still be paying.</summary>
    public static readonly TimeSpan CheckAfter = TimeSpan.FromMinutes(15);

    /// <summary>An unpaid checkout is closed at the gateway after this. A payor who comes back later simply gets a new one.</summary>
    public static readonly TimeSpan CloseAfter = TimeSpan.FromHours(24);

    /// <summary>Oldest first. Older checkouts close and leave the list, so newer ones wait one pass at most in practice.</summary>
    public const int MaxPerPass = 50;

    public async Task ReconcileAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await transactions.GetByIdAsync(transactionId, cancellationToken);
        if (transaction is not { IsResumable: true, GatewayReference: { } checkoutId })
            return;

        var read = await gateway.RetrievePaymentStatusAsync(checkoutId, cancellationToken);
        if (!read.IsSuccess || read.Value is null)
            return;

        var evt = read.Value;
        var everyAttemptFailed = evt.Type == PaymentGatewayEventType.Failed;
        if (evt.Type is PaymentGatewayEventType.Unknown or PaymentGatewayEventType.Failed
            && clock.UtcNow - transaction.CreatedAt >= CloseAfter)
        {
            var closed = await checkoutExpiry.ExpireAndVerifyAsync(checkoutId, cancellationToken);
            if (!closed.IsSuccess || closed.Value is null)
                return;
            evt = closed.Value;
        }

        switch (evt.Type)
        {
            case PaymentGatewayEventType.Paid:
                await settlement.SettleAsync(transaction, evt, cancellationToken);
                break;

            case PaymentGatewayEventType.Expired:
                if (everyAttemptFailed)
                    transaction.MarkFailed(evt.RawPayload);
                else
                    transaction.MarkExpired(evt.RawPayload);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;
        }
    }
}
