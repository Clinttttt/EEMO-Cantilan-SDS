namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// A lightweight realtime alert that an online payment was received. Carries only what a toast needs
/// plus the stall + billing period so admin facility pages can refresh the exact row that just got
/// paid. Transport-agnostic (SignalR is an Infrastructure/API concern behind <c>IOnlinePaymentNotifier</c>).
/// </summary>
public sealed record OnlinePaymentNotification(
    string Reference,
    decimal Amount,
    string Period,
    string? Method,
    DateTime PaidAtUtc,
    Guid StallId,
    int BillingYear,
    int BillingMonth);
