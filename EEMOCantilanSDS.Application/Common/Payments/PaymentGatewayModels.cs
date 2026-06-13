namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// Provider-agnostic request to open a hosted checkout session. Amounts are expressed in
/// Philippine pesos (major units, e.g. 250.00); the gateway implementation is responsible for
/// converting to the provider's smallest-unit representation (centavos for PayMongo).
/// </summary>
public sealed record CreateCheckoutSessionRequest(
    decimal Amount,
    string Reference,
    string Description,
    string SuccessUrl,
    string CancelUrl,
    string? CustomerName = null,
    string? CustomerEmail = null);

/// <summary>
/// Result of opening a hosted checkout session: where to redirect the payor and the opaque
/// gateway reference (checkout session id) used later to correlate webhook events.
/// </summary>
public sealed record CheckoutSessionResult(
    string CheckoutUrl,
    string GatewayReference,
    string Provider);

/// <summary>
/// Normalized webhook event types. Provider-specific event names are mapped onto these so the
/// payment lifecycle (Phase 2) stays provider-agnostic.
/// </summary>
public enum PaymentGatewayEventType
{
    /// <summary>An event we do not act on (kept for idempotency/audit).</summary>
    Unknown = 0,
    Paid = 1,
    Failed = 2,
    Expired = 3
}

/// <summary>
/// Normalized representation of a provider webhook event. <see cref="Amount"/> is in pesos.
/// <see cref="RawPayload"/> is retained verbatim for audit and idempotency.
/// </summary>
public sealed record PaymentGatewayEvent(
    PaymentGatewayEventType Type,
    string GatewayReference,
    decimal Amount,
    string? PaymentId,
    string? Method,
    DateTime? PaidAt,
    string RawPayload);
