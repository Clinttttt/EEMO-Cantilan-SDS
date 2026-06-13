namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// A lightweight realtime alert that an online payment was received. Carries only what a toast needs;
/// full detail lives in the staff reconciliation list. Transport-agnostic (SignalR is an Infrastructure/
/// API concern behind <c>IOnlinePaymentNotifier</c>).
/// </summary>
public sealed record OnlinePaymentNotification(
    string Reference,
    decimal Amount,
    string Period,
    string? Method,
    DateTime PaidAtUtc);
