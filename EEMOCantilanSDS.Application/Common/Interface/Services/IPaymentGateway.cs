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

    /// <summary>Verifies the webhook signature against an EXPLICIT secret (per-LGU webhook). The two-arg
    /// overload uses the global configured secret (the default LGU, Cantilan).</summary>
    bool VerifyWebhookSignature(string payload, string signatureHeader, string webhookSecret);

    /// <summary>Parses a raw webhook payload into a normalized <see cref="PaymentGatewayEvent"/>.</summary>
    Result<PaymentGatewayEvent> ParseEvent(string payload);

    /// <summary>
    /// Reconciliation fallback: queries the provider directly for the live status of a previously
    /// created checkout session (by its gateway reference) and returns it as a normalized
    /// <see cref="PaymentGatewayEvent"/>. Used when a webhook never arrived — the payor's return,
    /// or a staff reconcile, verifies the real payment state with the server secret key.
    /// Returns <see cref="PaymentGatewayEventType.Paid"/> when settled, <c>Expired</c> when the
    /// session expired, or <c>Unknown</c> when still pending/unpaid.
    /// </summary>
    Task<Result<PaymentGatewayEvent>> RetrievePaymentStatusAsync(
        string gatewayReference,
        CancellationToken cancellationToken = default);
}
