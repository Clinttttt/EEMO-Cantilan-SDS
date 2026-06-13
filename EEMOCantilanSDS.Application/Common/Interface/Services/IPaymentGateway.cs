using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Swappable abstraction over an online payment provider (PayMongo today). Kept provider-agnostic
/// so the online-payment lifecycle never depends on a concrete gateway. The system handles no card
/// data — only hosted checkout — so no PCI scope is introduced.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Provider identifier persisted on the transaction (e.g. "PayMongo").</summary>
    string Provider { get; }

    /// <summary>
    /// Opens a hosted checkout session for the given amount/reference and returns the redirect URL
    /// plus the gateway reference used to correlate later webhook events.
    /// </summary>
    Task<Result<CheckoutSessionResult>> CreateCheckoutSessionAsync(
        CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the authenticity of a webhook payload against the provider signature header.
    /// Fails closed (returns false) when no signing secret is configured.
    /// </summary>
    bool VerifyWebhookSignature(string payload, string signatureHeader);

    /// <summary>Parses a raw webhook payload into a normalized <see cref="PaymentGatewayEvent"/>.</summary>
    Result<PaymentGatewayEvent> ParseEvent(string payload);
}
