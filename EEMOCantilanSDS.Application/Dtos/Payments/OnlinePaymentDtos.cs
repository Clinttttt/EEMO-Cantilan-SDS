using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>Returned to the payor after initiating: where to redirect, plus the internal reference.</summary>
public sealed record InitiateOnlinePaymentResultDto(string CheckoutUrl, string Reference);

/// <summary>
/// A reconciliation row for staff: an online payment whose money has been received but whose
/// Official Receipt has not yet been encoded.
/// </summary>
public sealed record OnlinePaymentAwaitingOrDto(
    Guid TransactionId,
    string Reference,
    FacilityCode Facility,
    string StallNo,
    string PayorName,
    string Period,
    decimal Amount,
    string? Method,
    DateTime? PaidAt);

/// <summary>
/// Treasury overview + recent history for the admin Online Payments page. All figures are drawn from the
/// LGU's OWN settled online-payment records (not the payment gateway), so they reconcile to the treasury
/// report. "Settled" = money received (Paid or OR-completed).
/// </summary>
public sealed record OnlinePaymentDashboardDto(
    decimal CollectedThisMonth,
    decimal CollectedThisYear,
    int SettledCountThisYear,
    string? TopMethod,
    int Year,
    IReadOnlyList<OnlinePaymentHistoryItemDto> Recent);

/// <summary>One received online payment for the history table (payor + facility resolved).</summary>
public sealed record OnlinePaymentHistoryItemDto(
    string Reference,
    string PayorName,
    string Facility,
    string StallNo,
    string Period,
    decimal Amount,
    string? Method,
    string Status,
    string? ORNumber,
    DateTime? PaidAt);
