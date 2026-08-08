namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// A lightweight realtime alert that an online payment was received. Carries only what a toast needs
/// plus the stall + billing period so admin facility pages can refresh the exact row that just got
/// paid. Transport-agnostic (SignalR is an Infrastructure/API concern behind <c>IOnlinePaymentNotifier</c>).
/// </summary>
/// <param name="TenantCode">
/// Which LGU the payment belongs to. Carried on the message rather than read from the ambient user, because a
/// settlement can arrive on a gateway callback that has no signed-in user to ask. The transport uses it to address
/// the alert: it was previously broadcast to every connected client, so every LGU's staff saw the peso amounts of
/// every other LGU's payments.
/// </param>
public sealed record OnlinePaymentNotification(
    string Reference,
    decimal Amount,
    string Period,
    string? Method,
    DateTime PaidAtUtc,
    Guid StallId,
    int BillingYear,
    int BillingMonth,
    string TenantCode = "");
